using System.Text;
using System.Text.RegularExpressions;
using DistillForge.Core.Segmentation;

namespace DistillForge.Core.Extraction;

/// <summary>A quote resolved to an exact [Start, End) span in the canonical source.</summary>
public sealed record ResolvedEvidence(string SegmentLabel, string Quote, int Start, int End, MatchQuality Quality);

/// <summary>
/// Resolves a model-supplied quote to source offsets within its segment.
/// Fallback chain: exact → normalised (whitespace, quotes, dashes, case; ellipsis-aware) → whole segment.
/// Fuzzy matching is a planned extra rung between normalised and segment.
/// </summary>
public static partial class QuoteResolver
{
    [GeneratedRegex(@"\.\.\.|…")]
    private static partial Regex Ellipsis();

    public static ResolvedEvidence Resolve(Segment segment, string? quote)
    {
        var q = quote?.Trim() ?? "";
        if (q.Length == 0) return WholeSegment(segment, q);

        int exact = segment.Text.IndexOf(q, StringComparison.Ordinal);
        if (exact >= 0)
        {
            return new ResolvedEvidence(segment.Label, q, segment.Start + exact, segment.Start + exact + q.Length, MatchQuality.Exact);
        }

        var (normSeg, map) = Normalise(segment.Text);
        var pieces = Ellipsis().Split(q)
            .Select(p => Normalise(p).Text.Trim(TrimChars))
            .Where(p => p.Length > 0)
            .ToList();

        int searchFrom = 0, firstStart = -1, lastEnd = -1;
        foreach (var piece in pieces)
        {
            int idx = normSeg.IndexOf(piece, searchFrom, StringComparison.Ordinal);
            if (idx < 0) return WholeSegment(segment, q);
            if (firstStart < 0) firstStart = map[idx];
            lastEnd = map[idx + piece.Length - 1] + 1;
            searchFrom = idx + piece.Length;
        }

        if (firstStart < 0) return WholeSegment(segment, q);
        return new ResolvedEvidence(segment.Label, q, segment.Start + firstStart, segment.Start + lastEnd, MatchQuality.Normalised);
    }

    // Wrapping quote marks and trailing punctuation models like to add or drop.
    private static readonly char[] TrimChars = ['"', '\'', ' ', '.', ',', ';', ':'];

    private static ResolvedEvidence WholeSegment(Segment segment, string quote) =>
        new(segment.Label, quote, segment.Start, segment.End, MatchQuality.Segment);

    /// <summary>
    /// Lower-cases, folds typographic quotes/dashes and collapses whitespace runs.
    /// <c>Map[i]</c> is the index in <paramref name="s"/> of normalised character <c>i</c>.
    /// </summary>
    internal static (string Text, int[] Map) Normalise(string s)
    {
        var sb = new StringBuilder(s.Length);
        var map = new List<int>(s.Length);
        bool lastWasSpace = true; // drops leading whitespace

        for (int i = 0; i < s.Length; i++)
        {
            char c = Fold(s[i]);
            if (char.IsWhiteSpace(c))
            {
                if (!lastWasSpace)
                {
                    sb.Append(' ');
                    map.Add(i);
                    lastWasSpace = true;
                }
                continue;
            }
            sb.Append(char.ToLowerInvariant(c));
            map.Add(i);
            lastWasSpace = false;
        }

        if (sb.Length > 0 && sb[^1] == ' ')
        {
            sb.Length--;
            map.RemoveAt(map.Count - 1);
        }
        return (sb.ToString(), map.ToArray());
    }

    private static char Fold(char c) => c switch
    {
        '‘' or '’' or '‚' or '‛' or '′' => '\'',
        '“' or '”' or '„' or '‟' or '″' => '"',
        '‐' or '‑' or '‒' or '–' or '—' or '―' => '-',
        ' ' => ' ',
        _ => c,
    };
}
