using DistillForge.Core.Segmentation;
using DistillForge.Core.Text;
using DistillForge.Data;
using DistillForge.Data.Entities;
using Microsoft.EntityFrameworkCore;
using SegmentEntity = DistillForge.Data.Entities.Segment;

namespace DistillForge.Api.Services;

public sealed class SourceValidationException(string message) : Exception(message);

/// <summary>Ingest: normalise once, pick a strategy, segment, persist. Source text is immutable afterwards.</summary>
public sealed class WorkspaceService(DistillForgeDbContext db)
{
    public const int MaxSourceChars = 2_000_000;

    public async Task<Workspace> CreateAsync(string sourceText, string? title, SegmentationStrategyKind? strategy, CancellationToken ct)
    {
        var source = Prepare(sourceText);
        var now = DateTimeOffset.UtcNow;
        var workspace = new Workspace
        {
            Id = Guid.CreateVersion7(),
            Title = string.IsNullOrWhiteSpace(title) ? DefaultTitle(source) : title.Trim(),
            SourceText = source,
            CreatedAt = now,
            UpdatedAt = now,
        };
        ApplySegments(workspace, strategy);

        db.Workspaces.Add(workspace);
        await db.SaveChangesAsync(ct);
        return workspace;
    }

    /// <summary>
    /// Replaces the source wholesale. Per the spec this wipes categories, notes and previous
    /// extraction runs; the UI is responsible for warning the user first.
    /// </summary>
    public async Task<Workspace?> ReplaceSourceAsync(Guid id, string sourceText, SegmentationStrategyKind? strategy, CancellationToken ct)
    {
        var source = Prepare(sourceText);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var workspace = await db.Workspaces.FirstOrDefaultAsync(w => w.Id == id && !w.IsArchived, ct);
        if (workspace is null) return null;

        await db.ActionItems.Where(a => a.WorkspaceId == id).ExecuteDeleteAsync(ct);
        await db.Categories.Where(c => c.WorkspaceId == id).ExecuteDeleteAsync(ct);
        await db.ExtractionRuns.Where(r => r.WorkspaceId == id).ExecuteDeleteAsync(ct);
        await db.Segments.Where(s => s.WorkspaceId == id).ExecuteDeleteAsync(ct);

        workspace.SourceText = source;
        workspace.UpdatedAt = DateTimeOffset.UtcNow;
        ApplySegments(workspace, strategy);
        // The workspace is already tracked and the new segments carry pre-assigned Guid keys, so EF
        // would treat them as existing rows (Modified -> UPDATE -> concurrency error). Mark them Added.
        db.Segments.AddRange(workspace.Segments);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return workspace;
    }

    private static string Prepare(string? sourceText)
    {
        if (string.IsNullOrWhiteSpace(sourceText)) throw new SourceValidationException("Source text is empty.");
        var source = SourceNormaliser.Normalise(sourceText);
        if (source.Length > MaxSourceChars)
            throw new SourceValidationException($"Source text is too long ({source.Length:N0} chars; max {MaxSourceChars:N0}).");
        return source;
    }

    private static void ApplySegments(Workspace workspace, SegmentationStrategyKind? strategy)
    {
        workspace.Strategy = strategy ?? StrategyDetector.Detect(workspace.SourceText);
        var segments = new Segmenter().Split(workspace.SourceText, workspace.Strategy);
        workspace.Segments = segments.Select(s => new SegmentEntity
        {
            Id = Guid.CreateVersion7(),
            WorkspaceId = workspace.Id,
            Ordinal = s.Ordinal,
            Label = s.Label,
            Start = s.Start,
            End = s.End,
            TokenEstimate = s.TokenEstimate,
        }).ToList();
    }

    private static string DefaultTitle(string source)
    {
        var firstLine = source.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? "Untitled";
        return firstLine.Length <= 80 ? firstLine : firstLine[..77] + "...";
    }
}
