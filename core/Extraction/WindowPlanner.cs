using DistillForge.Core.Segmentation;

namespace DistillForge.Core.Extraction;

/// <summary>A contiguous run of segments sent to the model in one call. Indexes are 0-based into the segment list.</summary>
public sealed record ExtractionWindow(int Index, int Count, IReadOnlyList<Segment> Segments);

/// <summary>
/// Splits a segmented document into windows that each fit a token budget. Consecutive windows
/// overlap by roughly <c>overlapFraction</c> of the budget so an action point that straddles a
/// boundary is seen whole by at least one window; the duplicates this causes are merged afterwards
/// by <see cref="ActionPointDeduplicator"/>.
/// </summary>
public static class WindowPlanner
{
    /// <summary>Per-segment prompt overhead (label + separators); keep in step with the prompt builder.</summary>
    public const int PerSegmentOverhead = 8;

    public static IReadOnlyList<ExtractionWindow> Plan(
        IReadOnlyList<Segment> segments, int maxWindowTokens, double overlapFraction = 0.12)
    {
        if (segments.Count == 0) return [];
        if (maxWindowTokens <= 0) throw new ArgumentOutOfRangeException(nameof(maxWindowTokens));

        int overlapTokens = (int)(maxWindowTokens * Math.Clamp(overlapFraction, 0, 0.5));
        var ranges = new List<(int Start, int End)>();
        int start = 0;

        while (true)
        {
            // Always take at least one segment, so a single oversized segment can't stall planning
            // (the extractor will reject it with a clear error).
            int end = start, used = 0;
            while (end < segments.Count)
            {
                int cost = Cost(segments[end]);
                if (end > start && used + cost > maxWindowTokens) break;
                used += cost;
                end++;
            }
            ranges.Add((start, end));
            if (end >= segments.Count) break;

            // Step back from the boundary until the overlap budget is used, but always advance.
            int next = end, overlap = 0;
            while (next > start + 1 && overlap + Cost(segments[next - 1]) <= overlapTokens)
            {
                overlap += Cost(segments[next - 1]);
                next--;
            }
            start = next;
        }

        return ranges
            .Select((r, i) => new ExtractionWindow(i + 1, ranges.Count, segments.Skip(r.Start).Take(r.End - r.Start).ToList()))
            .ToList();
    }

    private static int Cost(Segment s) => s.TokenEstimate + PerSegmentOverhead;
}
