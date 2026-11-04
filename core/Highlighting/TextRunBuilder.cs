using DistillForge.Core.Extraction;

namespace DistillForge.Core.Highlighting;

public sealed record HighlightRange(int Start, int End, MatchQuality Quality);

/// <summary>A run of source text; <see cref="Highlight"/> is null for unhighlighted text.</summary>
public sealed record TextRun(int Start, int End, MatchQuality? Highlight);

/// <summary>
/// Splits a text of the given length into runs around highlight ranges, for rendering
/// spans in a plain viewer. Overlaps are merged; the best (lowest) quality wins.
/// </summary>
public static class TextRunBuilder
{
    public static IReadOnlyList<TextRun> Build(int length, IEnumerable<HighlightRange> ranges)
    {
        var valid = ranges
            .Select(r => new HighlightRange(Math.Clamp(r.Start, 0, length), Math.Clamp(r.End, 0, length), r.Quality))
            .Where(r => r.End > r.Start)
            .ToList();

        var bounds = new SortedSet<int> { 0, length };
        foreach (var r in valid)
        {
            bounds.Add(r.Start);
            bounds.Add(r.End);
        }

        var runs = new List<TextRun>();
        int prev = -1;
        foreach (var b in bounds)
        {
            if (prev >= 0 && b > prev)
            {
                MatchQuality? best = null;
                foreach (var r in valid)
                {
                    if (r.Start <= prev && r.End >= b && (best is null || r.Quality < best)) best = r.Quality;
                }

                if (runs.Count > 0 && runs[^1].Highlight == best && runs[^1].End == prev)
                    runs[^1] = runs[^1] with { End = b };
                else
                    runs.Add(new TextRun(prev, b, best));
            }
            prev = b;
        }
        return runs;
    }
}
