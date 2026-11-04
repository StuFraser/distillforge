using System.Text.Json.Serialization;

namespace DistillForge.Core.Segmentation;

/// <summary>The first rung of the segmentation ladder, chosen per document type.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SegmentationStrategyKind>))]
public enum SegmentationStrategyKind
{
    /// <summary>Prose and plans: split on blank lines (paragraphs).</summary>
    Prose,

    /// <summary>Meeting transcripts: every non-blank line (speaker turn) is a unit.</summary>
    Transcript,
}
