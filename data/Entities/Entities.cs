using DistillForge.Core.Contracts;
using DistillForge.Core.Extraction;
using DistillForge.Core.Integrations;
using DistillForge.Core.Segmentation;

namespace DistillForge.Data.Entities;

/// <summary>
/// One source document and everything derived from it. SourceText is the canonical,
/// normalised text; it is never edited, only replaced wholesale (which clears all children).
/// </summary>
public class Workspace
{
    public Guid Id { get; set; }
    /// <summary>The signed-in user who owns this workspace. Stamped on save; null only on rows that pre-date auth (invisible to everyone until claimed).</summary>
    public Guid? OwnerId { get; set; }
    public required string Title { get; set; }
    /// <summary>Short lookup code (bare; shown as W-xxxxxx). Assigned at creation, never changes, unique.</summary>
    public required string Code { get; set; }
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
    /// <summary>Short lookup code (bare; shown as N-xxxxxx). Assigned at creation, never changes, unique.</summary>
    public required string Code { get; set; }
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

    /// <summary>How aggressively pass 2 merges duplicates. Null (older runs) means Medium.</summary>
    public MergeLevel? MergeLevel { get; set; }

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

/// <summary>An OpenAI-compatible endpoint (Ollama, LM Studio, OpenAI, OpenRouter...). Per user: each user brings their own.</summary>
public class AiProvider
{
    public Guid Id { get; set; }
    public Guid? OwnerId { get; set; }
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
    /// <summary>Same as the provider's owner, denormalised so "one default model per user" can be a unique index.</summary>
    public Guid? OwnerId { get; set; }
    public Guid AiProviderId { get; set; }
    public required string ModelId { get; set; }
    public string? DisplayName { get; set; }
    public int ContextTokens { get; set; } = 8192;
    public int ReserveOutput { get; set; } = 2048;
    public ResponseFormatMode ResponseFormat { get; set; } = ResponseFormatMode.JsonSchema;
    /// <summary>Default sends no reasoning_effort; Off/Low/Medium/High send it (reasoning models such as Qwen3 or Gemma 4).</summary>
    public ThinkingMode Thinking { get; set; } = ThinkingMode.Default;
    /// <summary>At most one model is the default (enforced by a partial unique index).</summary>
    public bool IsDefault { get; set; }

    public AiProvider? Provider { get; set; }
}

/// <summary>
/// A signed-in person, identified by the Entra ID tenant + object id from their token (never by email, which can change).
/// Rows are created on first sign-in. <see cref="Id"/> is what owner columns point at.
/// </summary>
public class AppUser
{
    public Guid Id { get; set; }
    /// <summary>The token's <c>tid</c> claim.</summary>
    public required string TenantId { get; set; }
    /// <summary>The token's <c>oid</c> claim: stable per user per tenant.</summary>
    public required string ObjectId { get; set; }
    public string? DisplayName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
    /// <summary>Usage level. Held here, not in the identity provider, so the provider stays swappable. New users start as <see cref="UserTier.Lite"/>.</summary>
    public UserTier Tier { get; set; } = UserTier.Lite;
}

public enum UserTier { Lite = 0, Full = 1 }

public enum DuplicateSource { Ai, Similarity }

/// <summary>A "these two notes may be the same task" flag. Pairs are stored once, with A &lt; B.</summary>
public class ActionItemLink
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Workspace? Workspace { get; set; }
    public Guid ActionItemAId { get; set; }
    public Guid ActionItemBId { get; set; }
    public DuplicateSource Source { get; set; }
    /// <summary>The user said "not a duplicate".</summary>
    public bool Dismissed { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// A user's connection to an external platform notes can be pushed to (GitHub, Jira…). Non-secret settings are plain JSON;
/// secrets (tokens) are one encrypted JSON blob (see SecretProtector) and never returned to the client.
/// </summary>
public class Integration
{
    public Guid Id { get; set; }
    public Guid? OwnerId { get; set; }
    public IntegrationPlatform Platform { get; set; }
    public required string Name { get; set; }
    /// <summary>JSON object of the platform's non-secret fields, keyed as in IntegrationCatalog.</summary>
    public string SettingsJson { get; set; } = "{}";
    /// <summary>Encrypted JSON object of the secret fields; null when there are none.</summary>
    public string? SecretsEncrypted { get; set; }
    /// <summary>Which secret fields have a value, so the UI can say "saved" without decrypting anything.</summary>
    public List<string> SecretKeys { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Records that a note was pushed through a connection, so a second push can skip it and the UI can link to the result.</summary>
public class NotePush
{
    public Guid Id { get; set; }
    public Guid? OwnerId { get; set; }
    public Guid ActionItemId { get; set; }
    public Guid IntegrationId { get; set; }
    /// <summary>The platform's id for what was created (e.g. the work item number).</summary>
    public required string ExternalId { get; set; }
    public string? ExternalUrl { get; set; }
    public required string ItemType { get; set; }
    /// <summary>The project (or equivalent) it was pushed to; null for platforms without one.</summary>
    public string? Scope { get; set; }
    public DateTimeOffset PushedAt { get; set; }
}
