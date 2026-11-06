using DistillForge.Core.Segmentation;

namespace DistillForge.Core.Extraction;

/// <summary>
/// Abstraction over the AI provider. Implementations send labelled segments to a model
/// and return its raw structured response; validation and offset resolution happen in code.
/// One call handles one <see cref="ExtractionWindow"/>; splitting a large document into windows
/// and merging the results is the caller's job.
/// </summary>
public interface IActionExtractor
{
    /// <summary>Tokens available for one prompt (system + segments), i.e. the size a window must fit.</summary>
    int PromptBudgetTokens { get; }

    Task<ExtractionResult> ExtractAsync(ExtractionWindow window, CancellationToken cancellationToken = default);

    /// <summary>
    /// Pass 2: asks the model which candidates (from different windows) describe the same task.
    /// Candidates must fit <see cref="PromptBudgetTokens"/>; see <see cref="EstimateConsolidationTokens"/>.
    /// </summary>
    Task<ConsolidationResult> ConsolidateAsync(IReadOnlyList<ConsolidationCandidate> candidates, CancellationToken cancellationToken = default);

    int EstimateConsolidationTokens(IReadOnlyList<ConsolidationCandidate> candidates);
}

public sealed record TokenUsage(int? PromptTokens, int? CompletionTokens, int? TotalTokens);

public sealed record ExtractionResult(
    ExtractionResponse Response,
    string Model,
    string? FinishReason,
    TokenUsage? Usage,
    IReadOnlyList<string> Warnings);
