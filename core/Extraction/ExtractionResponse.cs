using System.Text.Json.Serialization;

namespace DistillForge.Core.Extraction;

/// <summary>
/// The structured JSON the model is asked to return. The model never returns
/// offsets: only segment IDs and verbatim quotes, which are resolved in code.
/// </summary>
public sealed class ExtractionResponse
{
    [JsonPropertyName("action_points")]
    public List<ExtractedActionPoint> ActionPoints { get; set; } = [];
}

public sealed class ExtractedActionPoint
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("summary")]
    public string? Summary { get; set; }

    [JsonPropertyName("owner")]
    public string? Owner { get; set; }

    [JsonPropertyName("due")]
    public string? Due { get; set; }

    [JsonPropertyName("evidence")]
    public List<ExtractedEvidence> Evidence { get; set; } = [];
}

public sealed class ExtractedEvidence
{
    [JsonPropertyName("segment_id")]
    public string? SegmentId { get; set; }

    [JsonPropertyName("quote")]
    public string? Quote { get; set; }
}
