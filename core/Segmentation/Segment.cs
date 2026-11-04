namespace DistillForge.Core.Segmentation;

/// <summary>
/// A contiguous, non-overlapping slice of the canonical source text.
/// <see cref="Text"/> is always exactly <c>source[Start..End]</c>.
/// </summary>
/// <param name="Label">Stable ID shown to the model, e.g. "S12".</param>
/// <param name="Ordinal">1-based position in the document.</param>
public sealed record Segment(string Label, int Ordinal, int Start, int End, string Text, int TokenEstimate)
{
    public int Length => End - Start;

    public static string LabelFor(int ordinal) => $"S{ordinal}";
}
