using DistillForge.Core.Contracts;
using DistillForge.Core.Extraction;
using DistillForge.Core.Segmentation;

namespace DistillForge.Data.Entities;

/// <summary>
/// One source document and everything derived from it. SourceText is the canonical,
/// normalised text; it is never edited, only replaced wholesale (which clears all children).
/// </summary>
public class Workspace
{
    public Guid Id { get; set; }
    public required string Title { get; set; }
    public required string SourceText { get; set; }
    public SegmentationStrategyKind Strategy { get; set; }
    public bool IsArchived { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public List<Segment> Segments { get; set; } = [];
    public List<Category> Categories { get; set; } = [];
    public List<ActionItem> ActionItems { get; set; } = [];
    public List<ExtractionRun> ExtractionRuns { get; set; } = [];
}

/// <summary>Segment boundaries against Workspace.SourceText. Text is derived, not stored.</summary>
public class Segment
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public int Ordinal { get; set; }
    public required string Label { get; set; }
    public int Start { get; set; }
    public int End { get; set; }
    public int TokenEstimate { get; set; }

    public Workspace? Workspace { get; set; }
}

public class Category
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public required string Name { get; set; }
    public string? Colour { get; set; }
    public int SortOrder { get; set; }

    public Workspace? Workspace { get; set; }
}

public class ActionItem
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid? CategoryId { get; set; }
    public Guid? ExtractionRunId { get; set; }
    /// <summary>Set when this note was merged into another (its parent). Merged notes stay as full records, are hidden in the UI and follow the parent's category.</summary>
    public Guid? MergedIntoId { get; set; }
    public required string Title { get; set; }
    public string? Summary { get; set; }
    public string? Owner { get; set; }
    public string? Due { get; set; }
    public bool IsManual { get; set; }
    /// <summary>One of <see cref="NoteColours.Keys"/>, or null for automatic (pink, yellow when it has a deadline).</summary>
    public string? Colour { get; set; }
    public int SortOrder { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public Workspace? Workspace { get; set; }
    public Category? Category { get; set; }
    public List<Evidence> Evidence { get; set; } = [];
}

public class Evidence
{
    public Guid Id { get; set; }
    public Guid ActionItemId { get; set; }
    public Guid SegmentId { get; set; }
    public required string Quote { get; set; }
    public int Start { get; set; }
    public int End { get; set; }
    public MatchQuality MatchQuality { get; set; }

    public ActionItem? ActionItem { get; set; }
    public Segment? Segment { get; set; }
}

public class ExtractionRun
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public ExtractionRunStatus Status { get; set; }
    public string? Model { get; set; }
    public string? FinishReason { get; set; }
    public int? PromptTokens { get; set; }
    public int? CompletionTokens { get; set; }
    public int? EstimatedPromptTokens { get; set; }
    public int? ActionItemCount { get; set; }
    public string? Error { get; set; }
    public string? Warnings { get; set; }

    // Live progress, updated as the run works through its passes.
    /// <summary>The model the run was asked to use. Null once that model is deleted (the Model string keeps the history) or when the default/config fallback was used.</summary>
    public Guid? AiModelId { get; set; }

    public ExtractionPhase? Phase { get; set; }
    public int? WindowIndex { get; set; }
    public int? WindowCount { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>Stamped every few seconds while a worker is executing the run. A Running run whose heartbeat has gone quiet was orphaned (host killed); see RunStaleness.</summary>
    public DateTimeOffset? HeartbeatAt { get; set; }

    public Workspace? Workspace { get; set; }
}

/// <summary>An OpenAI-compatible endpoint (Ollama, LM Studio, OpenAI, OpenRouter...). Owner column arrives with auth.</summary>
public class AiProvider
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    /// <summary>Includes the version path, e.g. http://host:11434/v1. Stored without a trailing slash.</summary>
    public required string BaseUrl { get; set; }
    /// <summary>Encrypted (see SecretProtector). Never returned to the client.</summary>
    public string? ApiKeyEncrypted { get; set; }
    public int TimeoutSeconds { get; set; } = 300;
    public DateTimeOffset CreatedAt { get; set; }

    public List<AiModel> Models { get; set; } = [];
}

/// <summary>A model on a provider, with the limits that are properties of the model rather than the endpoint.</summary>
public class AiModel
{
    public Guid Id { get; set; }
    public Guid AiProviderId { get; set; }
    public required string ModelId { get; set; }
    public string? DisplayName { get; set; }
    public int ContextTokens { get; set; } = 8192;
    public int ReserveOutput { get; set; } = 2048;
    public ResponseFormatMode ResponseFormat { get; set; } = ResponseFormatMode.JsonSchema;
    /// <summary>At most one model is the default (enforced by a partial unique index).</summary>
    public bool IsDefault { get; set; }

    public AiProvider? Provider { get; set; }
}

public enum DuplicateSource { Ai, Similarity }

/// <summary>A "these two notes may be the same task" flag. Pairs are stored once, with A &lt; B.</summary>
public class ActionItemLink
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid ActionItemAId { get; set; }
    public Guid ActionItemBId { get; set; }
    public DuplicateSource Source { get; set; }
    /// <summary>The user said "not a duplicate".</summary>
    public bool Dismissed { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
