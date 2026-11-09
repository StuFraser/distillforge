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
                .SetProperty(r => r.StartedAt, started), hostCt);
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
        var watcher = WatchForCancelAsync(runId, userCancel, watcherStop.Token);
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

            int windowBudget = extractor.PromptBudgetTokens - Ai.ExtractionPrompt.FixedOverhead;
            if (windowBudget <= 0)
                throw new Ai.ExtractionFailedException(
                    $"Prompt budget ({extractor.PromptBudgetTokens} tokens) is too small for the extraction prompt; raise the model's context tokens in AI settings.");
            var windows = WindowPlanner.Plan(segments, windowBudget);
            run.EstimatedPromptTokens = windows.Sum(w => Ai.ExtractionPrompt.EstimateTokens(w.Segments));
            logger.LogInformation("Run {RunId}: {Segments} segments in {Windows} window(s), window budget {Budget} tokens.",
                runId, segments.Count, windows.Count, windowBudget);

            var warnings = new List<string>();
            var found = new List<ValidatedActionPoint>();
            var aiPossible = new List<(ValidatedActionPoint A, ValidatedActionPoint B)>(); // flagged by the model, never merged
            int? promptTokens = null, completionTokens = null;
            string? model = null, finishReason = null;
            string? lastFailure = null;
            int failed = 0;

            foreach (var window in windows)
            {
                var label = windows.Count > 1 ? $"Window {window.Index}/{window.Count}: " : "";
                await ReportProgress(run, ExtractionPhase.Extracting, window.Index, window.Count, ct);
                try
                {
                    var result = await extractor.ExtractAsync(window, ct);
                    // Validate against this window's segments only: evidence must come from what the model was shown.
                    var outcome = ExtractionValidator.Validate(result.Response, window.Segments);
                    found.AddRange(outcome.ActionPoints);
                    warnings.AddRange(result.Warnings.Concat(outcome.Warnings).Select(w => label + w));

                    model = result.Model;
                    finishReason = result.FinishReason;
                    promptTokens = Add(promptTokens, result.Usage?.PromptTokens);
                    completionTokens = Add(completionTokens, result.Usage?.CompletionTokens);
                }
                catch (Exception ex) when (windows.Count > 1 && !ct.IsCancellationRequested
                    && ex is Ai.ExtractionFailedException or HttpRequestException or TaskCanceledException)
                {
                    // One bad window shouldn't discard the others' (expensive) results.
                    failed++;
                    lastFailure = ex.Message;
                    logger.LogError(ex, "Run {RunId}: window {Index}/{Count} failed.", runId, window.Index, window.Count);
                    warnings.Add($"{label}failed ({ex.Message}); notes from this part of the document are missing.");
                }
            }

            if (failed == windows.Count)
                throw new Ai.ExtractionFailedException($"Every window failed. Last error: {lastFailure}");

            // Overlapping windows report boundary items twice; merge the obvious ones in code first.
            var merged = ActionPointDeduplicator.Merge(found.OrderBy(ap => ap.Evidence[0].Start)).ToList();
            if (merged.Count < found.Count)
                logger.LogInformation("Run {RunId}: merged {Merged} duplicate action point(s) across windows.", runId, found.Count - merged.Count);

            // Pass 2: ask the model to spot paraphrased duplicates. Only worth it when windows overlapped.
            if (windows.Count > 1 && merged.Count > 1)
            {
                await ReportProgress(run, ExtractionPhase.Consolidating, windows.Count, windows.Count, ct);
                try
                {
                    var candidates = ActionPointConsolidator.ToCandidates(merged);
                    int estimate = extractor.EstimateConsolidationTokens(candidates);
                    if (estimate > extractor.PromptBudgetTokens)
                    {
                        warnings.Add($"Consolidation skipped: {candidates.Count} candidates (~{estimate} tokens) exceed the prompt budget; duplicates may remain.");
                    }
                    else
                    {
                        var result = await extractor.ConsolidateAsync(candidates, ct);
                        var outcome = ActionPointConsolidator.Apply(merged, result.Response);
                        foreach (var (a, b) in ActionPointConsolidator.PossiblePairs(result.Response, outcome.FinalIndexOf))
                            aiPossible.Add((outcome.ActionPoints[a], outcome.ActionPoints[b]));
                        merged = outcome.ActionPoints.ToList();
                        warnings.AddRange(result.Warnings.Concat(outcome.Warnings).Select(w => "Consolidation: " + w));
                        promptTokens = Add(promptTokens, result.Usage?.PromptTokens);
                        completionTokens = Add(completionTokens, result.Usage?.CompletionTokens);
                        logger.LogInformation("Run {RunId}: consolidation merged {Merged} further action point(s).", runId, outcome.MergedCount);
                    }
                }
                catch (Exception ex) when (!ct.IsCancellationRequested
                    && ex is Ai.ExtractionFailedException or HttpRequestException or TaskCanceledException)
                {
                    // Non-fatal: the deterministic merge above is still a usable result.
                    logger.LogError(ex, "Run {RunId}: consolidation failed.", runId);
                    warnings.Add($"Consolidation pass failed ({ex.Message}); duplicates may remain.");
                }
            }
            merged = merged.OrderBy(ap => ap.Evidence[0].Start).ToList();

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

    /// <summary>Polls (on its own DbContext; ours isn't thread-safe) and fires <paramref name="userCancel"/> when the run is marked Cancelled.</summary>
    private async Task WatchForCancelAsync(Guid runId, CancellationTokenSource userCancel, CancellationToken stop)
    {
        try
        {
            while (!stop.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), stop);
                await using var scope = scopes.CreateAsyncScope();
                var scoped = scope.ServiceProvider.GetRequiredService<DistillForgeDbContext>();
                var status = await scoped.ExtractionRuns.AsNoTracking()
                    .Where(r => r.Id == runId).Select(r => r.Status).FirstOrDefaultAsync(stop);
                if (status == ExtractionRunStatus.Cancelled) { userCancel.Cancel(); return; }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { logger.LogWarning(ex, "Cancel watcher for run {RunId} stopped; a user cancel will not interrupt this run.", runId); }
    }

    /// <summary>Persists live progress so the polling client can show where the run is up to.</summary>
    private async Task ReportProgress(ExtractionRun run, ExtractionPhase phase, int index, int count, CancellationToken ct)
    {
        run.Phase = phase;
        run.WindowIndex = index;
        run.WindowCount = count;
        await db.SaveChangesAsync(ct);
    }

    private static int? Add(int? total, int? value) => value is null ? total : (total ?? 0) + value;

    private static string? Clip(string? s, int max) => s is null || s.Length <= max ? s : s[..max];
}
