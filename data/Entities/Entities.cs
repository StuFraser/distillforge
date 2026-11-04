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
    public required string Title { get; set; }
    public string? Summary { get; set; }
    public string? Owner { get; set; }
    public string? Due { get; set; }
    public bool IsManual { get; set; }
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
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    public Workspace? Workspace { get; set; }
}
