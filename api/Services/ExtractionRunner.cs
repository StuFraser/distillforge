using DistillForge.Core.Contracts;
using DistillForge.Core.Extraction;
using DistillForge.Data;
using DistillForge.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DistillForge.Api.Services;

/// <summary>
/// Pipeline: plan windows → per window: extract → validate (segment IDs, quotes) → resolve offsets →
/// merge duplicates from overlapping windows (pass 1), then an LLM consolidation pass (pass 2) → persist. Documents that fit the budget are one window.
/// </summary>
public sealed class ExtractionRunner(DistillForgeDbContext db, IActionExtractor extractor, ILogger<ExtractionRunner> logger)
{
    public async Task RunAsync(Guid runId, CancellationToken ct)
    {
        var run = await db.ExtractionRuns.FirstOrDefaultAsync(r => r.Id == runId, ct);
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

        run.Status = ExtractionRunStatus.Running;
        run.StartedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        try
        {
            var workspace = await db.Workspaces
                .Include(w => w.Segments)
                .FirstAsync(w => w.Id == run.WorkspaceId, ct);
            var segments = workspace.ToCoreSegments();

            int windowBudget = extractor.PromptBudgetTokens - Ai.ExtractionPrompt.FixedOverhead;
            if (windowBudget <= 0)
                throw new Ai.ExtractionFailedException(
                    $"Prompt budget ({extractor.PromptBudgetTokens} tokens) is too small for the extraction prompt; increase AiProvider:ContextTokens.");
            var windows = WindowPlanner.Plan(segments, windowBudget);
            run.EstimatedPromptTokens = windows.Sum(w => Ai.ExtractionPrompt.EstimateTokens(w.Segments));
            logger.LogInformation("Run {RunId}: {Segments} segments in {Windows} window(s), window budget {Budget} tokens.",
                runId, segments.Count, windows.Count, windowBudget);

            var warnings = new List<string>();
            var found = new List<ValidatedActionPoint>();
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
            foreach (var ap in merged)
            {
                db.ActionItems.Add(new ActionItem
                {
                    Id = Guid.CreateVersion7(),
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

            run.Model = model;
            run.FinishReason = finishReason;
            run.PromptTokens = promptTokens;
            run.CompletionTokens = completionTokens;
            run.ActionItemCount = merged.Count;
            run.Warnings = warnings.Count > 0 ? string.Join('\n', warnings) : null;
            run.Status = ExtractionRunStatus.Succeeded;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // Don't rethrow: a queue retry would repeat an expensive (and likely identical) model call.
            logger.LogError(ex, "Extraction run {RunId} failed.", runId);
            db.ChangeTracker.Clear();
            db.Attach(run);
            run.Status = ExtractionRunStatus.Failed;
            run.Error = ex is Ai.ExtractionFailedException or HttpRequestException or TaskCanceledException
                ? ex.Message
                : $"{ex.GetType().Name}: {ex.Message}";
        }

        run.CompletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(CancellationToken.None);
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
