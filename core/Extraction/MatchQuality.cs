using System.Text.Json.Serialization;

namespace DistillForge.Core.Extraction;

/// <summary>How precisely a piece of evidence was located in the source. Lower is better.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<MatchQuality>))]
public enum MatchQuality
{
    /// <summary>Quote found verbatim in its segment.</summary>
    Exact = 0,

    /// <summary>Quote found after normalising whitespace, quotes, dashes and case (or across an ellipsis).</summary>
    Normalised = 1,

    /// <summary>Quote not found; the whole segment is highlighted and flagged low confidence.</summary>
    Segment = 2,
}
