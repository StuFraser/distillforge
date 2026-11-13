using DistillForge.Api.Ai;
using DistillForge.Core.Extraction;
using DistillForge.Core.Segmentation;
using DistillForge.Core.Contracts;
using Microsoft.Extensions.Logging;

namespace DistillForge.Api.Services;

/// <summary>Counters describing what the pipeline did; surfaced by the benchmark harness.</summary>
public sealed record PipelineStats(
    int Windows,
    int FailedWindows,
    int WindowBudgetTokens,
    int EstimatedPromptTokens,
    int FoundBeforeMerge,
    int AfterWindowMerge,
    int ConsolidationMerged,
    bool ConsolidationRan,
    bool ConsolidationSkipped,
    bool ConsolidationFailed,
    int ConsolidationCalls = 0);

public sealed record PipelineResult(
    IReadOnlyList<ValidatedActionPoint> ActionPoints,
    IReadOnlyList<(ValidatedActionPoint A, ValidatedActionPoint B)> AiPossible,
    IReadOnlyList<string> Warnings,
    int? PromptTokens,
    int? CompletionTokens,
    string? Model,
    string? FinishReason,
    PipelineStats Stats);

/// <summary>
/// The database-free part of extraction: plan windows → per window: extract → validate (segment IDs, quotes) →
/// resolve offsets → merge duplicates from overlapping windows (pass 1) → LLM consolidation (pass 2).
/// <see cref="ExtractionRunner"/> wraps this with persistence and run bookkeeping; the benchmark harness calls it directly.
/// </summary>
public static class ExtractionPipeline
{
    public static async Task<PipelineResult> RunAsync(
        IReadOnlyList<DistillForge.Core.Segmentation.Segment> segments,
        IActionExtractor extractor,
        Func<ExtractionPhase, int, int, Task>? progress,
        ILogger logger,
        CancellationToken ct)
    {
        int windowBudget = extractor.PromptBudgetTokens - ExtractionPrompt.FixedOverhead;
        if (windowBudget <= 0)
            throw new ExtractionFailedException(
                $"Prompt budget ({extractor.PromptBudgetTokens} tokens) is too small for the extraction prompt; raise the model's context tokens in AI settings.");
        var windows = WindowPlanner.Plan(segments, windowBudget);
        int estimatedPromptTokens = windows.Sum(w => ExtractionPrompt.EstimateTokens(w.Segments));
        logger.LogInformation("{Segments} segments in {Windows} window(s), window budget {Budget} tokens.",
            segments.Count, windows.Count, windowBudget);

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
            if (progress is not null) await progress(ExtractionPhase.Extracting, window.Index, window.Count);
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
                && ex is ExtractionFailedException or HttpRequestException or TaskCanceledException)
            {
                // One bad window shouldn't discard the others' (expensive) results.
                failed++;
                lastFailure = ex.Message;
                logger.LogError(ex, "Window {Index}/{Count} failed.", window.Index, window.Count);
                warnings.Add($"{label}failed ({ex.Message}); notes from this part of the document are missing.");
            }
        }

        if (failed == windows.Count)
            throw new ExtractionFailedException($"Every window failed. Last error: {lastFailure}");

        // Overlapping windows report boundary items twice; merge the obvious ones in code first.
        var merged = ActionPointDeduplicator.Merge(found.OrderBy(ap => ap.Evidence[0].Start)).ToList();
        int afterWindowMerge = merged.Count;
        if (merged.Count < found.Count)
            logger.LogInformation("Merged {Merged} duplicate action point(s) across windows.", found.Count - merged.Count);

        // Pass 2: ask the model to spot paraphrased duplicates. Only worth it when windows overlapped.
        // A list too long for one prompt is consolidated in batches (see ConsolidationBatcher).
        bool ran = false, skipped = false, consolidationFailed = false;
        int consolidationMerged = 0, consolidationCalls = 0;
        if (windows.Count > 1 && merged.Count > 1)
        {
            if (progress is not null) await progress(ExtractionPhase.Consolidating, windows.Count, windows.Count);
            var batched = await ConsolidationBatcher.RunAsync(merged, extractor, logger, ct);
            consolidationCalls = batched.Calls;
            skipped = batched.Skipped;
            ran = batched.Calls > 0;
            consolidationFailed = batched.Calls > 0 && batched.FailedCalls == batched.Calls;
            warnings.AddRange(batched.Warnings);
            if (ran)
            {
                foreach (var (a, b) in batched.PossiblePairs)
                    aiPossible.Add((batched.Points[a], batched.Points[b]));
                merged = batched.Points.ToList();
                consolidationMerged = batched.MergedCount;
                promptTokens = Add(promptTokens, batched.PromptTokens);
                completionTokens = Add(completionTokens, batched.CompletionTokens);
                logger.LogInformation("Consolidation: {Calls} call(s), merged {Merged} further action point(s).",
                    batched.Calls, batched.MergedCount);
            }
        }
        merged = merged.OrderBy(ap => ap.Evidence[0].Start).ToList();

        return new PipelineResult(merged, aiPossible, warnings, promptTokens, completionTokens, model, finishReason,
            new PipelineStats(windows.Count, failed, windowBudget, estimatedPromptTokens, found.Count, afterWindowMerge,
                consolidationMerged, ran, skipped, consolidationFailed, consolidationCalls));
    }

    private static int? Add(int? total, int? value) => value is null ? total : (total ?? 0) + value;
}
