using DistillForge.Api.Ai;
using DistillForge.Core.Contracts;
using DistillForge.Core.Extraction;
using Microsoft.Extensions.Logging;

namespace DistillForge.Api.Services;

public sealed record BatchedConsolidation(
    IReadOnlyList<ValidatedActionPoint> Points,
    /// <summary>Pairs of indexes into <see cref="Points"/> the model flagged as possible duplicates (never merged).</summary>
    IReadOnlyList<(int A, int B)> PossiblePairs,
    int MergedCount,
    int Calls,
    int FailedCalls,
    IReadOnlyList<string> Warnings,
    int? PromptTokens,
    int? CompletionTokens,
    bool Skipped = false);

/// <summary>
/// Pass 2 for lists too long for one prompt: splits the (position-ordered) candidates into batches that each fit the
/// prompt budget, consolidates each batch, then runs a second round with the batch boundaries shifted by half a batch,
/// so duplicates that straddled a boundary in round 1 get compared. Duplicates come from overlapping neighbouring
/// windows, so they are nearly always close together in the position order.
/// A list that fits in one prompt is handled in exactly one call, as before.
/// </summary>
public static class ConsolidationBatcher
{
    /// <summary>Caps candidates per call so the reply (merge groups) stays small even on large contexts.</summary>
    public const int MaxCandidatesPerBatch = 40;

    public static async Task<BatchedConsolidation> RunAsync(
        IReadOnlyList<ValidatedActionPoint> points, IActionExtractor extractor, ILogger logger, CancellationToken ct,
        MergeLevel level = MergeLevel.Medium)
    {
        var state = new State(points.ToList());

        var firstRound = Partition(state.Points, extractor, firstLimit: null);
        if (firstRound.Count == 0 || firstRound.All(b => b.Count < 2))
        {
            state.Warnings.Add("Consolidation skipped: the prompt budget is too small to compare even two candidates; duplicates may remain.");
            return state.ToResult(skipped: true);
        }

        await RoundAsync(state, firstRound, extractor, logger, ct, level);

        // Only needed when round 1 split the list; otherwise the single call already saw everything.
        if (firstRound.Count > 1 && state.Points.Count > 1)
        {
            var second = Partition(state.Points, extractor, firstLimit: Math.Max(2, firstRound[0].Count / 2));
            await RoundAsync(state, second, extractor, logger, ct, level);
        }
        return state.ToResult(skipped: false);
    }

    /// <summary>Greedy partition of the list into contiguous batches (start, count) that fit the prompt budget.</summary>
    internal static List<(int Start, int Count)> Partition(
        IReadOnlyList<ValidatedActionPoint> points, IActionExtractor extractor, int? firstLimit)
    {
        var batches = new List<(int, int)>();
        int start = 0;
        while (start < points.Count)
        {
            int cap = batches.Count == 0 && firstLimit is { } f ? f : MaxCandidatesPerBatch;
            int count = 1;
            while (start + count < points.Count && count < cap)
            {
                var next = ActionPointConsolidator.ToCandidates(points.Skip(start).Take(count + 1).ToList());
                if (extractor.EstimateConsolidationTokens(next) > extractor.PromptBudgetTokens) break;
                count++;
            }
            batches.Add((start, count));
            start += count;
        }
        return batches;
    }

    private static async Task RoundAsync(State s, List<(int Start, int Count)> batches, IActionExtractor extractor,
        ILogger logger, CancellationToken ct, MergeLevel level)
    {
        var newPoints = new List<ValidatedActionPoint>();
        var oldToNew = new int[s.Points.Count];
        var newPairs = new HashSet<(int, int)>();

        foreach (var (start, count) in batches)
        {
            var slice = s.Points.Skip(start).Take(count).ToList();
            int baseIndex = newPoints.Count;

            if (count >= 2)
            {
                try
                {
                    var candidates = ActionPointConsolidator.ToCandidates(slice);
                    if (extractor.EstimateConsolidationTokens(candidates) <= extractor.PromptBudgetTokens)
                    {
                        s.Calls++;
                        var result = await extractor.ConsolidateAsync(candidates, level, ct);
                        var response = ActionPointConsolidator.ForLevel(result.Response, level);
                        // Medium refuses to auto-merge notes with clearly different owners; those get flagged instead.
                        var outcome = ActionPointConsolidator.Apply(slice, response, guardOwners: level == MergeLevel.Medium);
                        foreach (var (a, b) in ActionPointConsolidator.PossiblePairs(response, outcome.FinalIndexOf))
                            newPairs.Add((baseIndex + a, baseIndex + b));
                        foreach (var (a, b) in outcome.Blocked ?? [])
                        {
                            int x = outcome.FinalIndexOf[a], y = outcome.FinalIndexOf[b];
                            if (x != y) newPairs.Add((baseIndex + Math.Min(x, y), baseIndex + Math.Max(x, y)));
                        }
                        for (int i = 0; i < count; i++) oldToNew[start + i] = baseIndex + outcome.FinalIndexOf[i];
                        newPoints.AddRange(outcome.ActionPoints);
                        s.Merged += outcome.MergedCount;
                        s.Warnings.AddRange(result.Warnings.Concat(outcome.Warnings).Select(w => "Consolidation: " + w));
                        s.Prompt = Add(s.Prompt, result.Usage?.PromptTokens);
                        s.Completion = Add(s.Completion, result.Usage?.CompletionTokens);
                        continue;
                    }
                }
                catch (Exception ex) when (!ct.IsCancellationRequested
                    && ex is ExtractionFailedException or HttpRequestException or TaskCanceledException)
                {
                    s.FailedCalls++;
                    logger.LogError(ex, "Consolidation batch failed.");
                    s.Warnings.Add($"Consolidation batch failed ({ex.Message}); duplicates may remain in that part of the document.");
                }
            }

            // Single candidate, oversized batch or failed call: keep the slice as it is.
            for (int i = 0; i < count; i++) oldToNew[start + i] = baseIndex + i;
            newPoints.AddRange(slice);
        }

        // Carry earlier rounds' "possible" pairs across, dropping any that collapsed into one point.
        foreach (var (a, b) in s.Pairs)
        {
            int x = oldToNew[a], y = oldToNew[b];
            if (x != y) newPairs.Add(x < y ? (x, y) : (y, x));
        }
        s.Points = newPoints;
        s.Pairs = newPairs;
    }

    private static int? Add(int? total, int? value) => value is null ? total : (total ?? 0) + value;

    private sealed class State(List<ValidatedActionPoint> points)
    {
        public List<ValidatedActionPoint> Points = points;
        public HashSet<(int, int)> Pairs = [];
        public List<string> Warnings = [];
        public int Merged, Calls, FailedCalls;
        public int? Prompt, Completion;

        public BatchedConsolidation ToResult(bool skipped) => new(
            Points, Pairs.Order().ToList(), Merged, Calls, FailedCalls, Warnings, Prompt, Completion, skipped);
    }
}
