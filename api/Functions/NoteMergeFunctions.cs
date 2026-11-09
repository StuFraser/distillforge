using DistillForge.Api.Services;
using DistillForge.Core.Contracts;
using DistillForge.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.EntityFrameworkCore;

namespace DistillForge.Api.Functions;

/// <summary>
/// Merging is non-destructive: a merged note keeps all its data, gets MergedIntoId = parent, is hidden by the client and
/// follows the parent's category. Every endpoint returns the workspace's full note list so the client can simply replace its copy.
/// </summary>
public sealed class NoteMergeFunctions(DistillForgeDbContext db)
{
    private const string Archived = "This workspace is archived; unarchive it to merge notes.";

    [Function("MergeActionItem")]
    public async Task<IActionResult> Merge(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "actions/{id:guid}/merge")] HttpRequest req, Guid id, CancellationToken ct)
    {
        var body = await WorkspaceFunctions.ReadBody<MergeActionItemRequest>(req, ct);
        if (body is null) return new BadRequestObjectResult("Expected JSON body with targetId.");

        var item = await db.ActionItems.Include(a => a.Workspace).FirstOrDefaultAsync(a => a.Id == id, ct);
        if (item is null) return new NotFoundResult();
        if (item.Workspace!.IsArchived) return new ConflictObjectResult(Archived);
        if (item.MergedIntoId is not null) return new BadRequestObjectResult("That note is already merged into another; unmerge it first.");

        var all = await db.ActionItems.Where(a => a.WorkspaceId == item.WorkspaceId).ToListAsync(ct);
        var parentOf = all.ToDictionary(a => a.Id, a => a.MergedIntoId);
        if (NoteGraph.ValidateMerge(id, body.TargetId, parentOf) is { } error) return new BadRequestObjectResult(error);

        // Targets resolve to their top-level note, so merges never nest.
        var target = all.First(a => a.Id == NoteGraph.Root(body.TargetId, parentOf));
        foreach (var child in all.Where(a => a.MergedIntoId == id)) // its own merged notes move up to the new parent
        {
            child.MergedIntoId = target.Id;
            child.CategoryId = target.CategoryId;
        }
        item.MergedIntoId = target.Id;
        item.CategoryId = target.CategoryId;
        item.Workspace.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return new OkObjectResult(await ActionItemQueries.LoadDtosAsync(db, item.WorkspaceId, ct));
    }

    [Function("UnmergeActionItem")]
    public async Task<IActionResult> Unmerge(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "actions/{id:guid}/unmerge")] HttpRequest req, Guid id, CancellationToken ct)
    {
        var item = await db.ActionItems.Include(a => a.Workspace).FirstOrDefaultAsync(a => a.Id == id, ct);
        if (item is null) return new NotFoundResult();
        if (item.Workspace!.IsArchived) return new ConflictObjectResult(Archived);

        if (item.MergedIntoId is not null)
        {
            item.MergedIntoId = null; // keeps the category it inherited; the user can move it
            item.Workspace.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
        }
        return new OkObjectResult(await ActionItemQueries.LoadDtosAsync(db, item.WorkspaceId, ct));
    }

    /// <summary>Dismisses every flag between the two notes' groups (a flag can sit on a merged child, so compare roots).</summary>
    [Function("DismissDuplicate")]
    public async Task<IActionResult> NotDuplicate(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "actions/{id:guid}/not-duplicate")] HttpRequest req, Guid id, CancellationToken ct)
    {
        var body = await WorkspaceFunctions.ReadBody<NotDuplicateRequest>(req, ct);
        if (body is null) return new BadRequestObjectResult("Expected JSON body with otherId.");

        var item = await db.ActionItems.Include(a => a.Workspace).FirstOrDefaultAsync(a => a.Id == id, ct);
        if (item is null) return new NotFoundResult();
        if (item.Workspace!.IsArchived) return new ConflictObjectResult(Archived);

        var all = await db.ActionItems.AsNoTracking().Where(a => a.WorkspaceId == item.WorkspaceId).ToListAsync(ct);
        var parentOf = all.ToDictionary(a => a.Id, a => a.MergedIntoId);
        if (!parentOf.ContainsKey(body.OtherId)) return new NotFoundResult();
        var (ra, rb) = (NoteGraph.Root(id, parentOf), NoteGraph.Root(body.OtherId, parentOf));

        var links = await db.ActionItemLinks.Where(l => l.WorkspaceId == item.WorkspaceId && !l.Dismissed).ToListAsync(ct);
        foreach (var l in links)
        {
            var (x, y) = (NoteGraph.Root(l.ActionItemAId, parentOf), NoteGraph.Root(l.ActionItemBId, parentOf));
            if ((x == ra && y == rb) || (x == rb && y == ra)) l.Dismissed = true;
        }
        await db.SaveChangesAsync(ct);
        return new OkObjectResult(await ActionItemQueries.LoadDtosAsync(db, item.WorkspaceId, ct));
    }
}
