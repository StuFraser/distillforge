using DistillForge.Core.Contracts;
using DistillForge.Data;
using DistillForge.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace DistillForge.Api.Services;

public static class ActionItemQueries
{
    /// <summary>All notes in a workspace (merged ones included) with their possible-duplicate flags resolved.</summary>
    public static async Task<List<ActionItemDto>> LoadDtosAsync(DistillForgeDbContext db, Guid workspaceId, CancellationToken ct)
    {
        var items = await db.ActionItems.AsNoTracking()
            .Where(a => a.WorkspaceId == workspaceId)
            .Include(a => a.Evidence).ThenInclude(e => e.Segment)
            .OrderBy(a => a.SortOrder)
            .ToListAsync(ct);
        var links = await db.ActionItemLinks.AsNoTracking()
            .Where(l => l.WorkspaceId == workspaceId)
            .ToListAsync(ct);

        var flags = NoteGraph.PossibleDuplicates(
            items.Select(i => new NoteNode(i.Id, i.MergedIntoId)),
            links.Select(l => new DuplicateLink(l.ActionItemAId, l.ActionItemBId, l.Dismissed)));
        return items.Select(i => i.ToDto(flags.GetValueOrDefault(i.Id))).ToList();
    }
}
