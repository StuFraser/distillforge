using DistillForge.Api.Functions;
using DistillForge.Core.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DistillForge.Api.Tests;

public class DeleteActionItemTests : IDisposable
{
    private readonly TestDb _db = new();
    public void Dispose() => _db.Dispose();

    private async Task<IActionResult> Delete(Guid noteId)
    {
        await using var db = _db.NewContext();
        return await new ActionItemFunctions(db).Delete(Http.Empty(), noteId, CancellationToken.None);
    }

    [Fact]
    public async Task Deletes_the_note_and_returns_the_remaining_notes()
    {
        var w = _db.SeedWorkspace();
        var a = _db.SeedNote(w.Id, "a", 0);
        var b = _db.SeedNote(w.Id, "b", 1);

        var remaining = Http.Ok<List<ActionItemDto>>(await Delete(a.Id));

        Assert.Equal([b.Id], remaining.Select(n => n.Id));
        await using var db = _db.NewContext();
        Assert.Equal([b.Id], await db.ActionItems.Select(n => n.Id).ToListAsync());
    }

    [Fact]
    public async Task Unknown_note_is_404() =>
        Assert.IsType<NotFoundResult>(await Delete(Guid.NewGuid()));

    [Fact]
    public async Task Archived_workspace_is_409_and_the_note_survives()
    {
        var w = _db.SeedWorkspace(archived: true);
        var n = _db.SeedNote(w.Id);

        Assert.IsType<ConflictObjectResult>(await Delete(n.Id));

        await using var db = _db.NewContext();
        Assert.Single(db.ActionItems);
    }

    [Fact]
    public async Task Evidence_is_deleted_with_the_note_but_segments_are_kept()
    {
        var w = _db.SeedWorkspace();
        await using (var db = _db.NewContext())
        {
            var seg = await db.Segments.FirstAsync(s => s.WorkspaceId == w.Id);
            var n = _db.SeedNote(w.Id, evidenceSegmentId: seg.Id);
            Assert.Equal(1, await db.Evidence.CountAsync());
            await Delete(n.Id);
        }

        await using var after = _db.NewContext();
        Assert.Empty(after.Evidence);
        Assert.Equal(3, await after.Segments.CountAsync()); // the source's segments belong to the workspace, not the note
    }

    [Fact]
    public async Task Duplicate_flags_involving_the_note_are_deleted_but_other_flags_stay()
    {
        var w = _db.SeedWorkspace();
        var a = _db.SeedNote(w.Id, "a", 0);
        var b = _db.SeedNote(w.Id, "b", 1);
        var c = _db.SeedNote(w.Id, "c", 2);
        _db.SeedLink(w.Id, a.Id, b.Id);
        _db.SeedLink(w.Id, b.Id, c.Id);

        await Delete(a.Id);

        await using var db = _db.NewContext();
        var link = await db.ActionItemLinks.SingleAsync();
        Assert.Equal((b.Id, c.Id), (link.ActionItemAId, link.ActionItemBId));
    }

    [Fact]
    public async Task Notes_merged_into_a_deleted_parent_are_freed_not_deleted_and_keep_their_category()
    {
        var w = _db.SeedWorkspace();
        var cat = _db.SeedCategory(w.Id);
        var parent = _db.SeedNote(w.Id, "parent", 0, categoryId: cat.Id);
        var child = _db.SeedNote(w.Id, "child", 1, categoryId: cat.Id, mergedIntoId: parent.Id);

        var remaining = Http.Ok<List<ActionItemDto>>(await Delete(parent.Id));

        var freed = Assert.Single(remaining);
        Assert.Equal(child.Id, freed.Id);
        Assert.Null(freed.MergedIntoId);        // visible again
        Assert.Equal(cat.Id, freed.CategoryId);
    }

    [Fact]
    public async Task Deleting_a_merged_child_leaves_its_parent_alone()
    {
        var w = _db.SeedWorkspace();
        var parent = _db.SeedNote(w.Id, "parent", 0);
        var child = _db.SeedNote(w.Id, "child", 1, mergedIntoId: parent.Id);

        var remaining = Http.Ok<List<ActionItemDto>>(await Delete(child.Id));

        Assert.Equal([parent.Id], remaining.Select(n => n.Id));
    }

    [Fact]
    public async Task Only_the_target_workspaces_notes_are_returned_and_other_workspaces_are_untouched()
    {
        var w = _db.SeedWorkspace();
        var other = _db.SeedWorkspace();
        var mine = _db.SeedNote(w.Id, "mine", 0);
        var keep = _db.SeedNote(w.Id, "keep", 1);
        var theirs = _db.SeedNote(other.Id, "theirs", 0);

        var remaining = Http.Ok<List<ActionItemDto>>(await Delete(mine.Id));

        Assert.Equal([keep.Id], remaining.Select(n => n.Id));
        await using var db = _db.NewContext();
        Assert.True(await db.ActionItems.AnyAsync(n => n.Id == theirs.Id));
    }

    [Fact]
    public async Task Manual_and_extracted_notes_can_both_be_deleted()
    {
        var w = _db.SeedWorkspace();
        var manual = _db.SeedNote(w.Id, "manual", 0, manual: true);
        var extracted = _db.SeedNote(w.Id, "extracted", 1);

        await Delete(manual.Id);
        Assert.Empty((await ListIds(extracted.Id)).Except([extracted.Id]));
        await Delete(extracted.Id);

        await using var db = _db.NewContext();
        Assert.Empty(db.ActionItems);
    }

    [Fact]
    public async Task Deleting_bumps_the_workspaces_updated_time_and_a_second_delete_is_404()
    {
        var w = _db.SeedWorkspace();
        var n = _db.SeedNote(w.Id);

        await Delete(n.Id);

        await using (var db = _db.NewContext())
            Assert.True((await db.Workspaces.SingleAsync()).UpdatedAt > w.UpdatedAt);
        Assert.IsType<NotFoundResult>(await Delete(n.Id));
    }

    private async Task<List<Guid>> ListIds(Guid _)
    {
        await using var db = _db.NewContext();
        return await db.ActionItems.Select(n => n.Id).ToListAsync();
    }
}
