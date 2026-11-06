using DistillForge.Core.Contracts;
using DistillForge.Data.Entities;
using CoreSegment = DistillForge.Core.Segmentation.Segment;

namespace DistillForge.Api.Services;

public static class Mapping
{
    public static WorkspaceDto ToDto(this Workspace w) => new(
        w.Id, w.Title, w.SourceText, w.Strategy, w.IsArchived, w.CreatedAt,
        w.Segments.OrderBy(s => s.Ordinal).Select(s => new SegmentDto(s.Id, s.Label, s.Start, s.End)).ToList());

    public static WorkspaceSummaryDto ToSummaryDto(this Workspace w, int actionItemCount) =>
        new(w.Id, w.Title, w.IsArchived, actionItemCount, w.CreatedAt, w.UpdatedAt);

    public static ActionItemDto ToDto(this ActionItem a) => new(
        a.Id, a.Title, a.Summary, a.Owner, a.Due, a.CategoryId, a.IsManual, a.SortOrder,
        a.Evidence.OrderBy(e => e.Start)
            .Select(e => new EvidenceDto(e.Segment?.Label ?? "", e.Quote, e.Start, e.End, e.MatchQuality))
            .ToList());

    public static ExtractionRunDto ToDto(this ExtractionRun r) => new(
        r.Id, r.WorkspaceId, r.Status, r.Model, r.PromptTokens, r.CompletionTokens, r.ActionItemCount,
        r.Error, r.Warnings, r.CreatedAt, r.CompletedAt, r.Phase, r.WindowIndex, r.WindowCount);

    /// <summary>Rebuilds core segments (with text) from stored boundaries and the canonical source.</summary>
    public static IReadOnlyList<CoreSegment> ToCoreSegments(this Workspace w) =>
        w.Segments.OrderBy(s => s.Ordinal)
            .Select(s =>
            {
                var text = w.SourceText[s.Start..s.End];
                return new CoreSegment(s.Label, s.Ordinal, s.Start, s.End, text, s.TokenEstimate);
            })
            .ToList();
}
