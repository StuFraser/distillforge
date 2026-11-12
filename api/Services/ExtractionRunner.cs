using DistillForge.Core.Contracts;
using DistillForge.Core.Extraction;
using DistillForge.Data;
using DistillForge.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DistillForge.Api.Services;

/// <summary>
/// Pipeline: plan windows → per window: extract → validate (segment IDs, quotes) → resolve offsets →
/// merge duplicates from overlapping windows (pass 1), then an LLM consolidation pass (pass 2) → persist. Documents that fit the budget are one window.
/// </summary>
public sealed class ExtractionRunner(
    DistillForgeDbContext db, Ai.AiConfigResolver resolver, Ai.ActionExtractorFactory extractors,
    IServiceScopeFactory scopes, ILogger<ExtractionRunner> logger)
{
    public async Task RunAsync(Guid runId, CancellationToken hostCt)
    {
        var run = await db.ExtractionRuns.FirstOrDefaultAsync(r => r.Id == runId, hostCt);
        if (run is null)
        {
            logger.LogWarning("Extraction run {RunId} not found (workspace replaced?); skipping.", runId);
            return;
        }
        if (run.Status != ExtractionRunStatus.Queued)
        {
            logger.LogWarning("Extraction run {RunId} is {Status}, not Queued; skipping redelivery.", runId, run.Status);
            return;
        }

        // Claim the run atomically: a user cancel that lands between the check above and here must win.
        var started = DateTimeOffset.UtcNow;
        var claimed = await db.ExtractionRuns
            .Where(r => r.Id == runId && r.Status == ExtractionRunStatus.Queued)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, ExtractionRunStatus.Running)
                .SetProperty(r => r.StartedAt, started)
                .SetProperty(r => r.HeartbeatAt, started), hostCt);
        if (claimed == 0)
        {
            logger.LogInformation("Extraction run {RunId} was cancelled before it started.", runId);
            return;
        }
        await db.Entry(run).ReloadAsync(hostCt);

        // `ct` is cancelled by either the host (timeout / shutdown) or the user (a watcher sees Status = Cancelled).
        using var userCancel = new CancellationTokenSource();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(hostCt, userCancel.Token);
        var ct = linked.Token;
        using var watcherStop = CancellationTokenSource.CreateLinkedTokenSource(hostCt);
        var watcher = WatchRunAsync(runId, userCancel, watcherStop.Token);
        var cancelledByUser = false;

        try
        {
            var workspace = await db.Workspaces
                .Include(w => w.Segments)
                .FirstAsync(w => w.Id == run.WorkspaceId, ct);
            var segments = workspace.ToCoreSegments();

            var config = await resolver.ResolveAsync(run.AiModelId, ct);
            var extractor = extractors.Create(config);
            logger.LogInformation("Run {RunId}: using model {Model} at {Url}.", runId, config.Model, config.BaseUrl);

            var result = await ExtractionPipeline.RunAsync(
                segments, extractor,
                (phase, index, count) => ReportProgress(run, phase, index, count, ct),
                logger, ct);
            run.EstimatedPromptTokens = result.Stats.EstimatedPromptTokens;
            var merged = result.ActionPoints;
            var aiPossible = result.AiPossible;
            var warnings = result.Warnings;
            var model = result.Model;
            var finishReason = result.FinishReason;
            var promptTokens = result.PromptTokens;
            var completionTokens = result.CompletionTokens;

            // Re-running extraction replaces previously generated notes; manual notes are kept.
            await db.ActionItems.Where(a => a.WorkspaceId == workspace.Id && !a.IsManual).ExecuteDeleteAsync(ct);

            var segmentIds = workspace.Segments.ToDictionary(s => s.Label, s => s.Id);
            var now = DateTimeOffset.UtcNow;
            int order = 0;
            var itemIds = new Dictionary<ValidatedActionPoint, Guid>(ReferenceEqualityComparer.Instance);
            foreach (var ap in merged)
            {
                var itemId = Guid.CreateVersion7();
                itemIds[ap] = itemId;
                db.ActionItems.Add(new ActionItem
                {
                    Id = itemId,
                    WorkspaceId = workspace.Id,
                    ExtractionRunId = run.Id,
                    Title = Clip(ap.Title, 300)!,
                    Summary = ap.Summary,
                    Owner = Clip(ap.Owner, 200),
                    Due = Clip(ap.Due, 100),
                    SortOrder = order++,
                    CreatedAt = now,
                    Evidence = ap.Evidence.Select(e => new Evidence
                    {
                        Id = Guid.CreateVersion7(),
                        SegmentId = segmentIds[e.SegmentLabel],
                        Quote = e.Quote,
                        Start = e.Start,
                        End = e.End,
                        MatchQuality = e.Quality,
                    }).ToList(),
                });
            }

            // Possible duplicates: the model's loose groups plus a whole-document title check. Flags only; nothing is merged here.
            var flags = new Dictionary<(Guid, Guid), DuplicateSource>();
            void Flag(Guid x, Guid y, DuplicateSource source)
            {
                if (x == y) return;
                var key = x.CompareTo(y) < 0 ? (x, y) : (y, x);
                if (!flags.ContainsKey(key) || source == DuplicateSource.Ai) flags[key] = source;
            }
            foreach (var (a, b) in aiPossible) Flag(itemIds[a], itemIds[b], DuplicateSource.Ai);
            foreach (var (a, b) in ActionPointDeduplicator.SimilarPairs(merged)) Flag(itemIds[merged[a]], itemIds[merged[b]], DuplicateSource.Similarity);
            db.Set<ActionItemLink>().AddRange(flags.Select(f => new ActionItemLink
            {
                Id = Guid.CreateVersion7(),
                WorkspaceId = workspace.Id,
                ActionItemAId = f.Key.Item1,
                ActionItemBId = f.Key.Item2,
                Source = f.Value,
                CreatedAt = now,
            }));
            logger.LogInformation("Run {RunId}: flagged {Count} possible duplicate pair(s).", runId, flags.Count);

            run.Model = model;
            run.FinishReason = finishReason;
            run.PromptTokens = promptTokens;
            run.CompletionTokens = completionTokens;
            run.ActionItemCount = merged.Count;
            run.Warnings = warnings.Count > 0 ? string.Join('\n', warnings) : null;
            run.Status = ExtractionRunStatus.Succeeded;
        }
        catch (OperationCanceledException) when (userCancel.IsCancellationRequested && !hostCt.IsCancellationRequested)
        {
            // The cancel endpoint already recorded Status = Cancelled. Notes are only written at the very end, so nothing to undo.
            cancelledByUser = true;
            logger.LogInformation("Extraction run {RunId} cancelled by the user.", runId);
        }
        catch (OperationCanceledException) when (hostCt.IsCancellationRequested)
        {
            // Function timeout (host.json functionTimeout) or host shutdown. Without this the run would stay "Running" forever.
            logger.LogError("Extraction run {RunId} was interrupted by the host (timeout or shutdown) at window {Index}/{Count}.",
                runId, run.WindowIndex, run.WindowCount);
            MarkFailed(run, $"Extraction was interrupted (the function timed out or the host shut down){ProgressSuffix(run)}.");
        }
        catch (Exception ex)
        {
            // Don't rethrow: a queue retry would repeat an expensive (and likely identical) model call.
            logger.LogError(ex, "Extraction run {RunId} failed.", runId);
            MarkFailed(run, ex is Ai.ExtractionFailedException or HttpRequestException or TaskCanceledException
                ? ex.Message
                : $"{ex.GetType().Name}: {ex.Message}");
        }

        watcherStop.Cancel();
        await watcher;
        if (cancelledByUser) return;

        run.CompletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(CancellationToken.None);
    }

    private static string ProgressSuffix(ExtractionRun run) =>
        run.WindowCount is > 0 ? $" at {(run.Phase == ExtractionPhase.Consolidating ? "consolidation" : $"window {run.WindowIndex}/{run.WindowCount}")}" : "";

    private void MarkFailed(ExtractionRun run, string error)
    {
        db.ChangeTracker.Clear();
        db.Attach(run);
        run.Status = ExtractionRunStatus.Failed;
        run.Error = error;
    }

    /// <summary>
    /// Runs alongside the run on its own DbContext (ours isn't thread-safe). Every 2 s it fires <paramref name="userCancel"/>
    /// if the run was marked Cancelled; every <see cref="RunStaleness.HeartbeatInterval"/> it stamps the run's heartbeat so a
    /// hard-killed host can be detected (see <see cref="ExtractionRunReaper"/>). A transient error must not end the loop, or the
    /// heartbeat would stop and a healthy run would be reaped.
    /// </summary>
    private async Task WatchRunAsync(Guid runId, CancellationTokenSource userCancel, CancellationToken stop)
    {
        var lastBeat = DateTimeOffset.UtcNow;
        int consecutiveErrors = 0;
        while (!stop.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(2), stop);
                await using var scope = scopes.CreateAsyncScope();
                var scoped = scope.ServiceProvider.GetRequiredService<DistillForgeDbContext>();
                var status = await scoped.ExtractionRuns.AsNoTracking()
                    .Where(r => r.Id == runId).Select(r => r.Status).FirstOrDefaultAsync(stop);
                if (status == ExtractionRunStatus.Cancelled) { userCancel.Cancel(); return; }

                var now = DateTimeOffset.UtcNow;
                if (now - lastBeat >= RunStaleness.HeartbeatInterval)
                {
                    await scoped.ExtractionRuns
                        .Where(r => r.Id == runId && r.Status == ExtractionRunStatus.Running)
                        .ExecuteUpdateAsync(s => s.SetProperty(r => r.HeartbeatAt, now), stop);
                    lastBeat = now;
                }
                consecutiveErrors = 0;
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                // Log the first failure and then every 10th, so a long database outage doesn't flood the log.
                if (consecutiveErrors++ % 10 == 0)
                    logger.LogWarning(ex, "Run {RunId}: cancel/heartbeat check failed ({Count} in a row); will keep trying.", runId, consecutiveErrors);
            }
        }
    }

    /// <summary>Persists live progress so the polling client can show where the run is up to.</summary>
    private async Task ReportProgress(ExtractionRun run, ExtractionPhase phase, int index, int count, CancellationToken ct)
    {
        run.Phase = phase;
        run.WindowIndex = index;
        run.WindowCount = count;
        await db.SaveChangesAsync(ct);
    }

    private static string? Clip(string? s, int max) => s is null || s.Length <= max ? s : s[..max];
}
