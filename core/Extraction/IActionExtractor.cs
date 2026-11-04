using DistillForge.Core.Segmentation;

namespace DistillForge.Core.Extraction;

/// <summary>
/// Abstraction over the AI provider. Implementations send labelled segments to a model
/// and return its raw structured response; validation and offset resolution happen in code.
/// </summary>
public interface IActionExtractor
{
    Task<ExtractionResult> ExtractAsync(IReadOnlyList<Segment> segments, CancellationToken cancellationToken = default);
}

public sealed record TokenUsage(int? PromptTokens, int? CompletionTokens, int? TotalTokens);

public sealed record ExtractionResult(
    ExtractionResponse Response,
    string Model,
    string? FinishReason,
    TokenUsage? Usage,
    IReadOnlyList<string> Warnings);
