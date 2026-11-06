using System.Text.Json.Serialization;
using DistillForge.Core.Extraction;
using DistillForge.Core.Segmentation;

namespace DistillForge.Core.Contracts;

public sealed record CreateWorkspaceRequest(string SourceText, string? Title = null, SegmentationStrategyKind? Strategy = null);

public sealed record ReplaceSourceRequest(string SourceText, SegmentationStrategyKind? Strategy = null);

public sealed record WorkspaceSummaryDto(
    Guid Id,
    string Title,
    bool IsArchived,
    int ActionItemCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record WorkspaceDto(
    Guid Id,
    string Title,
    string SourceText,
    SegmentationStrategyKind Strategy,
    bool IsArchived,
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
    IReadOnlyList<EvidenceDto> Evidence,
    string? Colour = null);

public sealed record EvidenceDto(string SegmentLabel, string Quote, int Start, int End, MatchQuality MatchQuality);

[JsonConverter(typeof(JsonStringEnumConverter<ExtractionRunStatus>))]
public enum ExtractionRunStatus
{
    Queued,
    Running,
    Succeeded,
    Failed,
}

/// <summary>What a running extraction is currently doing. Pass 1 = Extracting (window by window), pass 2 = Consolidating.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ExtractionPhase>))]
public enum ExtractionPhase
{
    Extracting,
    Consolidating,
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
    DateTimeOffset? CompletedAt,
    ExtractionPhase? Phase = null,
    int? WindowIndex = null,
    int? WindowCount = null);
