namespace DistillForge.Core.Segmentation;

/// <summary>
/// First-level split of a document into structural units. Units larger than the
/// segment limit are split further by <see cref="Segmenter"/> (sentence, then whitespace).
/// </summary>
public interface ISegmentationStrategy
{
    SegmentationStrategyKind Kind { get; }

    /// <summary>Whether tiny units (headings, one-liners) may be merged into the next unit.</summary>
    bool MergeTinyUnits { get; }

    /// <summary>Returns unit ranges [Start, End) in document order. Ranges may include surrounding whitespace.</summary>
    IEnumerable<(int Start, int End)> SplitUnits(string source);
}

public static class SegmentationStrategies
{
    public static ISegmentationStrategy For(SegmentationStrategyKind kind) => kind switch
    {
        SegmentationStrategyKind.Prose => new ProseStrategy(),
        SegmentationStrategyKind.Transcript => new TranscriptStrategy(),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    /// <summary>Yields each line's range, excluding the terminating '\n'.</summary>
    internal static IEnumerable<(int Start, int End)> Lines(string source)
    {
        int start = 0;
        while (start <= source.Length)
        {
            int nl = source.IndexOf('\n', start);
            int end = nl < 0 ? source.Length : nl;
            yield return (start, end);
            if (nl < 0) yield break;
            start = nl + 1;
        }
    }

    internal static bool IsBlank(string source, int start, int end)
    {
        for (int i = start; i < end; i++)
        {
            if (!char.IsWhiteSpace(source[i])) return false;
        }
        return true;
    }
}

/// <summary>Blank-line paragraph split.</summary>
public sealed class ProseStrategy : ISegmentationStrategy
{
    public SegmentationStrategyKind Kind => SegmentationStrategyKind.Prose;
    public bool MergeTinyUnits => true;

    public IEnumerable<(int Start, int End)> SplitUnits(string source)
    {
        int? paraStart = null;
        int paraEnd = 0;
        foreach (var (start, end) in SegmentationStrategies.Lines(source))
        {
            if (SegmentationStrategies.IsBlank(source, start, end))
            {
                if (paraStart is int ps)
                {
                    yield return (ps, paraEnd);
                    paraStart = null;
                }
                continue;
            }
            paraStart ??= start;
            paraEnd = end;
        }
        if (paraStart is int last) yield return (last, paraEnd);
    }
}

/// <summary>One unit per non-blank line (speaker turn). Tiny turns are kept separate.</summary>
public sealed class TranscriptStrategy : ISegmentationStrategy
{
    public SegmentationStrategyKind Kind => SegmentationStrategyKind.Transcript;
    public bool MergeTinyUnits => false;

    public IEnumerable<(int Start, int End)> SplitUnits(string source) =>
        SegmentationStrategies.Lines(source).Where(l => !SegmentationStrategies.IsBlank(source, l.Start, l.End));
}
