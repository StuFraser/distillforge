using System.Text.Json.Serialization;
using DistillForge.Core.Extraction;
using DistillForge.Core.Segmentation;

namespace DistillForge.Core.Contracts;

public sealed record CreateWorkspaceRequest(string SourceText, string? Title = null, SegmentationStrategyKind? Strategy = null);

public sealed record ReplaceSourceRequest(string SourceText, SegmentationStrategyKind? Strategy = null);

public sealed record WorkspaceSummaryDto(Guid Id, string Title, DateTimeOffset CreatedAt);

public sealed record WorkspaceDto(
    Guid Id,
    string Title,
    string SourceText,
    SegmentationStrategyKind Strategy,
    DateTimeOffset CreatedAt,
    IReadOnlyList<SegmentDto> Segments);

public sealed record SegmentDto(Guid Id, string Label, int Start, int End);

public sealed record ActionItemDto(
    Guid Id,
    string Title,
    string? Summary,
    string? Owner,
    string? Due,
    Guid? CategoryId,
    bool IsManual,
    int SortOrder,
    IReadOnlyList<EvidenceDto> Evidence);

public sealed record EvidenceDto(string SegmentLabel, string Quote, int Start, int End, MatchQuality MatchQuality);

[JsonConverter(typeof(JsonStringEnumConverter<ExtractionRunStatus>))]
public enum ExtractionRunStatus
{
    Queued,
    Running,
    Succeeded,
    Failed,
}

public sealed record ExtractionRunDto(
    Guid Id,
    Guid WorkspaceId,
    ExtractionRunStatus Status,
    string? Model,
    int? PromptTokens,
    int? CompletionTokens,
    int? ActionItemCount,
    string? Error,
    string? Warnings,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt);
