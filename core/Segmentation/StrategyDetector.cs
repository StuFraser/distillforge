using System.Text.RegularExpressions;

namespace DistillForge.Core.Segmentation;

/// <summary>
/// Cheap heuristic to pick a first-level strategy. The UI can override it.
/// </summary>
public static partial class StrategyDetector
{
    // Optional timestamp, then "Name:" at the start of a line.
    [GeneratedRegex(@"^\s*(\[?\(?\d{1,2}:\d{2}(:\d{2})?\]?\)?\s*[-–]?\s*)?[\p{Lu}][\p{L}\p{M} .'\-]{0,40}:\s", RegexOptions.CultureInvariant)]
    private static partial Regex SpeakerLine();

    public static SegmentationStrategyKind Detect(string source)
    {
        int nonBlank = 0, speaker = 0;
        foreach (var (start, end) in SegmentationStrategies.Lines(source))
        {
            if (SegmentationStrategies.IsBlank(source, start, end)) continue;
            nonBlank++;
            if (SpeakerLine().IsMatch(source.AsSpan(start, end - start))) speaker++;
        }

        return nonBlank >= 3 && speaker * 2 >= nonBlank
            ? SegmentationStrategyKind.Transcript
            : SegmentationStrategyKind.Prose;
    }
}
