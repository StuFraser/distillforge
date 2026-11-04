using DistillForge.Core.Text;

namespace DistillForge.Core.Segmentation;

public sealed record SegmenterOptions
{
    /// <summary>Hard maximum segment length in characters. A maximum, not a target.</summary>
    public int MaxChars { get; init; } = 512;

    /// <summary>Units shorter than this (headings, one-liners) merge into the next unit where the strategy allows.</summary>
    public int MinUnitChars { get; init; } = 40;
}

/// <summary>
/// Splits canonical source text into non-overlapping, labelled segments using a ladder:
/// structural unit (strategy) → sentences → last whitespace before the limit.
/// Offsets are always against the original string, so <c>source[Start..End] == Text</c>.
/// </summary>
public sealed class Segmenter(SegmenterOptions? options = null)
{
    private readonly SegmenterOptions _options = options ?? new SegmenterOptions();

    public IReadOnlyList<Segment> Split(string source, SegmentationStrategyKind kind) =>
        Split(source, SegmentationStrategies.For(kind));

    public IReadOnlyList<Segment> Split(string source, ISegmentationStrategy strategy)
    {
        ArgumentNullException.ThrowIfNull(source);
        int max = _options.MaxChars;

        var units = strategy.SplitUnits(source)
            .Select(u => Trim(source, u.Start, u.End))
            .Where(u => u.End > u.Start)
            .ToList();

        if (strategy.MergeTinyUnits) units = MergeTiny(units);

        var ranges = new List<(int Start, int End)>();
        foreach (var unit in units)
        {
            if (unit.End - unit.Start <= max) ranges.Add(unit);
            else ranges.AddRange(PackSentences(source, unit.Start, unit.End, max));
        }

        var segments = new List<Segment>(ranges.Count);
        foreach (var (start, end) in ranges)
        {
            int ordinal = segments.Count + 1;
            var text = source[start..end];
            segments.Add(new Segment(Segment.LabelFor(ordinal), ordinal, start, end, text, TokenEstimator.Estimate(text)));
        }
        return segments;
    }

    private List<(int Start, int End)> MergeTiny(List<(int Start, int End)> units)
    {
        var result = new List<(int Start, int End)>(units.Count);
        (int Start, int End)? carry = null;

        for (int i = 0; i < units.Count; i++)
        {
            var current = units[i];
            if (carry is { } c)
            {
                if (current.End - c.Start <= _options.MaxChars) current = (c.Start, current.End);
                else result.Add(c);
                carry = null;
            }

            bool isLast = i == units.Count - 1;
            if (!isLast && current.End - current.Start < _options.MinUnitChars)
            {
                carry = current;
                continue;
            }
            result.Add(current);
        }

        if (carry is { } remaining) result.Add(remaining);
        return result;
    }

    /// <summary>Splits an over-long unit at sentence boundaries, packing sentences up to the limit.</summary>
    private static IEnumerable<(int Start, int End)> PackSentences(string source, int start, int end, int max)
    {
        var pieces = SentenceSplitter.Split(source, start, end)
            .SelectMany(s => s.End - s.Start <= max ? [s] : HardSplit(source, s.Start, s.End, max));

        (int Start, int End)? current = null;
        foreach (var piece in pieces)
        {
            if (current is not { } c)
            {
                current = piece;
            }
            else if (piece.End - c.Start <= max)
            {
                current = (c.Start, piece.End);
            }
            else
            {
                yield return c;
                current = piece;
            }
        }
        if (current is { } last) yield return last;
    }

    /// <summary>
    /// Last rung: split at the last whitespace before the limit, never mid-word.
    /// A single "word" longer than the limit (e.g. a long URL) is cut at the limit.
    /// </summary>
    internal static IEnumerable<(int Start, int End)> HardSplit(string source, int start, int end, int max)
    {
        while (end - start > max)
        {
            int limit = start + max;
            int ws = -1;
            for (int i = limit; i > start; i--)
            {
                if (char.IsWhiteSpace(source[i])) { ws = i; break; }
            }

            int cut;
            if (ws > start)
            {
                cut = ws;
            }
            else
            {
                cut = limit;
                if (char.IsHighSurrogate(source[cut - 1])) cut--;
            }

            var piece = Trim(source, start, cut);
            if (piece.End > piece.Start) yield return piece;

            start = cut;
            while (start < end && char.IsWhiteSpace(source[start])) start++;
        }

        var tail = Trim(source, start, end);
        if (tail.End > tail.Start) yield return tail;
    }

    private static (int Start, int End) Trim(string source, int start, int end)
    {
        while (start < end && char.IsWhiteSpace(source[start])) start++;
        while (end > start && char.IsWhiteSpace(source[end - 1])) end--;
        return (start, end);
    }
}
