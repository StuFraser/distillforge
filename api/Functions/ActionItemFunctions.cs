using DistillForge.Api.Services;
using DistillForge.Core.Contracts;
using DistillForge.Core.Extraction;
using DistillForge.Data;
using DistillForge.Data.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.EntityFrameworkCore;

namespace DistillForge.Api.Functions;

public sealed class ActionItemFunctions(DistillForgeDbContext db)
{
    [Function("UpdateActionItem")]
    public async Task<IActionResult> Update(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "actions/{id:guid}")] HttpRequest req,
        Guid id,
        CancellationToken ct)
    {
        var body = await WorkspaceFunctions.ReadBody<UpdateActionItemRequest>(req, ct);
        if (body is null) return new BadRequestObjectResult("Expected JSON body with title, summary, owner, due and colour.");
        if (ActionItemValidation.Validate(body) is { } error) return new BadRequestObjectResult(error);

        var item = await db.ActionItems
            .Include(a => a.Workspace)
            .Include(a => a.Evidence).ThenInclude(e => e.Segment)
            .FirstOrDefaultAsync(a => a.Id == id, ct);
        if (item is null) return new NotFoundResult();
        if (item.Workspace!.IsArchived) return new ConflictObjectResult("This workspace is archived; unarchive it to edit notes.");

        item.Title = body.Title.Trim();
        item.Summary = Blank(body.Summary);
        item.Owner = Blank(body.Owner);
        item.Due = Blank(body.Due);
        item.Colour = body.Colour;
        item.Workspace.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        var dtos = await ActionItemQueries.LoadDtosAsync(db, item.WorkspaceId, ct);
        return new OkObjectResult(dtos.First(d => d.Id == id));
    }

    /// <summary>Adds a manual note (IsManual, so re-extraction leaves it alone). Returns the new note.</summary>
    [Function("CreateActionItem")]
    public async Task<IActionResult> Create(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "workspaces/{id:guid}/actions")] HttpRequest req,
        Guid id,
        CancellationToken ct)
    {
        var body = await WorkspaceFunctions.ReadBody<CreateActionItemRequest>(req, ct);
        if (body is null) return new BadRequestObjectResult("Expected JSON body with title, summary, owner, due, colour and categoryId.");
        if (ActionItemValidation.Validate(body) is { } error) return new BadRequestObjectResult(error);

        var workspace = await db.Workspaces.Include(w => w.Segments).FirstOrDefaultAsync(w => w.Id == id, ct);
        if (workspace is null) return new NotFoundResult();
        if (workspace.IsArchived) return new ConflictObjectResult("This workspace is archived; unarchive it to add notes.");
        if ((body.SourceStart is null) != (body.SourceEnd is null)
            || (body.SourceStart is int s0 && (s0 < 0 || body.SourceEnd <= s0 || body.SourceEnd > workspace.SourceText.Length)))
            return new BadRequestObjectResult("The selected passage is outside the source text.");
        if (body.CategoryId is Guid cid && !await db.Categories.AnyAsync(c => c.Id == cid && c.WorkspaceId == id, ct))
            return new BadRequestObjectResult("That category no longer exists.");

        var max = await db.ActionItems.Where(a => a.WorkspaceId == id).Select(a => (int?)a.SortOrder).MaxAsync(ct);
        var item = new ActionItem
        {
            Id = Guid.NewGuid(),
            WorkspaceId = id,
            CategoryId = body.CategoryId,
            Title = body.Title.Trim(),
            Summary = Blank(body.Summary),
            Owner = Blank(body.Owner),
            Due = Blank(body.Due),
            Colour = body.Colour,
            IsManual = true,
            SortOrder = (max ?? -1) + 1,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        // A passage selected in the source becomes the note's evidence: one entry per segment it touches, clipped to that segment.
        if (body is { SourceStart: int from, SourceEnd: int to })
        {
            foreach (var seg in workspace.Segments.OrderBy(s => s.Ordinal))
            {
                var (start, end) = (Math.Max(from, seg.Start), Math.Min(to, seg.End));
                if (end <= start) continue;
                var quote = workspace.SourceText[start..end];
                if (string.IsNullOrWhiteSpace(quote)) continue;
                item.Evidence.Add(new Evidence
                {
                    Id = Guid.CreateVersion7(),
                    SegmentId = seg.Id,
                    Quote = quote,
                    Start = start,
                    End = end,
                    MatchQuality = MatchQuality.Exact,
                });
            }
        }
        db.ActionItems.Add(item);
        workspace.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        var dtos = await ActionItemQueries.LoadDtosAsync(db, id, ct);
        return new OkObjectResult(dtos.First(d => d.Id == item.Id));
    }

    /// <summary>
    /// Deletes a note (its evidence and duplicate flags cascade). Notes merged into it are freed, not deleted (FK is SET NULL),
    /// and keep their category. Returns the workspace's full note list so the client can replace its copy.
    /// </summary>
    [Function("DeleteActionItem")]
    public async Task<IActionResult> Delete(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "actions/{id:guid}")] HttpRequest req,
        Guid id,
        CancellationToken ct)
    {
        var item = await db.ActionItems.Include(a => a.Workspace).FirstOrDefaultAsync(a => a.Id == id, ct);
        if (item is null) return new NotFoundResult();
        if (item.Workspace!.IsArchived) return new ConflictObjectResult("This workspace is archived; unarchive it to delete notes.");

        db.ActionItems.Remove(item);
        item.Workspace.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return new OkObjectResult(await ActionItemQueries.LoadDtosAsync(db, item.WorkspaceId, ct));
    }

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
