using DistillForge.Api.Functions;
using DistillForge.Core.Contracts;
using DistillForge.Core.Extraction;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DistillForge.Api.Tests;

public class CreateActionItemTests : IDisposable
{
    private readonly TestDb _db = new();
    public void Dispose() => _db.Dispose();

    private async Task<IActionResult> Create(Guid workspaceId, object? body)
    {
        await using var db = _db.NewContext();
        return await new ActionItemFunctions(db).Create(Http.Json(body), workspaceId, CancellationToken.None);
    }

    private static CreateActionItemRequest Req(string title = "Do the thing", string? summary = null, string? owner = null, string? due = null,
        string? colour = null, Guid? categoryId = null, int? start = null, int? end = null) =>
        new(title, summary, owner, due, colour, categoryId, start, end);

    [Fact]
    public async Task Creates_a_manual_note_in_Uncategorised_and_returns_it()
    {
        var w = _db.SeedWorkspace();

        var dto = Http.Ok<ActionItemDto>(await Create(w.Id, Req(title: "  Ring Sam  ", summary: "  about the rota ", owner: "Priya", due: "Friday", colour: "green")));

        Assert.Equal("Ring Sam", dto.Title);            // trimmed
        Assert.Equal("about the rota", dto.Summary);
        Assert.Equal("Priya", dto.Owner);
        Assert.Equal("Friday", dto.Due);
        Assert.Equal("green", dto.Colour);
        Assert.True(dto.IsManual);                      // so re-extract keeps it
        Assert.Null(dto.CategoryId);
        Assert.Empty(dto.Evidence);

        await using var db = _db.NewContext();
        var stored = await db.ActionItems.SingleAsync();
        Assert.Equal(dto.Id, stored.Id);
        Assert.True(stored.IsManual);
    }

    [Fact]
    public async Task Blank_optional_fields_are_stored_as_null()
    {
        var w = _db.SeedWorkspace();
        var dto = Http.Ok<ActionItemDto>(await Create(w.Id, Req(summary: "   ", owner: "", due: "\t")));
        Assert.Null(dto.Summary);
        Assert.Null(dto.Owner);
        Assert.Null(dto.Due);
    }

    [Fact]
    public async Task Sort_order_follows_the_workspaces_existing_notes()
    {
        var w = _db.SeedWorkspace();
        _db.SeedNote(w.Id, "a", sortOrder: 4);
        _db.SeedNote(w.Id, "b", sortOrder: 9);
        var other = _db.SeedWorkspace();
        _db.SeedNote(other.Id, "elsewhere", sortOrder: 100); // another workspace must not influence it

        var dto = Http.Ok<ActionItemDto>(await Create(w.Id, Req()));
        Assert.Equal(10, dto.SortOrder);
    }

    [Fact]
    public async Task First_note_in_an_empty_workspace_gets_sort_order_zero()
    {
        var w = _db.SeedWorkspace();
        Assert.Equal(0, Http.Ok<ActionItemDto>(await Create(w.Id, Req())).SortOrder);
    }

    [Fact]
    public async Task Note_can_be_created_inside_a_category()
    {
        var w = _db.SeedWorkspace();
        var c = _db.SeedCategory(w.Id);
        Assert.Equal(c.Id, Http.Ok<ActionItemDto>(await Create(w.Id, Req(categoryId: c.Id))).CategoryId);
    }

    [Fact]
    public async Task Category_from_another_workspace_is_rejected()
    {
        var w = _db.SeedWorkspace();
        var foreign = _db.SeedCategory(_db.SeedWorkspace().Id);

        var result = await Create(w.Id, Req(categoryId: foreign.Id));

        Assert.IsType<BadRequestObjectResult>(result);
        await using var db = _db.NewContext();
        Assert.Empty(db.ActionItems);
    }

    [Fact]
    public async Task Unknown_category_is_rejected() =>
        Assert.IsType<BadRequestObjectResult>(await Create(_db.SeedWorkspace().Id, Req(categoryId: Guid.NewGuid())));

    [Fact]
    public async Task Unknown_workspace_is_404() =>
        Assert.IsType<NotFoundResult>(await Create(Guid.NewGuid(), Req()));

    [Fact]
    public async Task Archived_workspace_is_409_and_nothing_is_written()
    {
        var w = _db.SeedWorkspace(archived: true);

        Assert.IsType<ConflictObjectResult>(await Create(w.Id, Req()));

        await using var db = _db.NewContext();
        Assert.Empty(db.ActionItems);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Missing_title_is_rejected(string title) =>
        Assert.IsType<BadRequestObjectResult>(await Create(_db.SeedWorkspace().Id, Req(title: title)));

    [Fact]
    public async Task Over_long_fields_and_unknown_colours_are_rejected()
    {
        var w = _db.SeedWorkspace().Id;
        Assert.IsType<BadRequestObjectResult>(await Create(w, Req(title: new string('x', ActionItemValidation.MaxTitle + 1))));
        Assert.IsType<BadRequestObjectResult>(await Create(w, Req(owner: new string('x', ActionItemValidation.MaxOwner + 1))));
        Assert.IsType<BadRequestObjectResult>(await Create(w, Req(colour: "chartreuse")));
        await using var db = _db.NewContext();
        Assert.Empty(db.ActionItems);
    }

    [Fact]
    public async Task Missing_or_malformed_body_is_400()
    {
        var w = _db.SeedWorkspace().Id;
        await using var db = _db.NewContext();
        var fn = new ActionItemFunctions(db);
        Assert.IsType<BadRequestObjectResult>(await fn.Create(Http.Empty(), w, CancellationToken.None));
        Assert.IsType<BadRequestObjectResult>(await fn.Create(Http.Json("not an object"), w, CancellationToken.None));
    }

    [Fact]
    public async Task Creating_a_note_bumps_the_workspaces_updated_time()
    {
        var w = _db.SeedWorkspace();
        await Create(w.Id, Req());
        await using var db = _db.NewContext();
        Assert.True((await db.Workspaces.SingleAsync()).UpdatedAt > w.UpdatedAt);
    }

    // ---- evidence from a selected passage (source: "AAAAAAAAAA\n\nBBBBBBBBBB\n\nCCCCCCCCCC"; S1 0-10, S2 12-22, S3 24-34) ----

    [Fact]
    public async Task Selection_inside_one_segment_becomes_one_exact_evidence_entry()
    {
        var w = _db.SeedWorkspace();

        var dto = Http.Ok<ActionItemDto>(await Create(w.Id, Req(start: 2, end: 6)));

        var e = Assert.Single(dto.Evidence);
        Assert.Equal("S1", e.SegmentLabel);
        Assert.Equal("AAAA", e.Quote);
        Assert.Equal((2, 6), (e.Start, e.End));
        Assert.Equal(MatchQuality.Exact, e.MatchQuality);
    }

    [Fact]
    public async Task Selection_spanning_segments_is_clipped_per_segment_and_skips_the_gap()
    {
        var w = _db.SeedWorkspace();

        // 8..14 covers the end of S1 (8-10), the blank gap (10-12), and the start of S2 (12-14).
        var dto = Http.Ok<ActionItemDto>(await Create(w.Id, Req(start: 8, end: 14)));

        Assert.Collection(dto.Evidence,
            e => { Assert.Equal("S1", e.SegmentLabel); Assert.Equal("AA", e.Quote); Assert.Equal((8, 10), (e.Start, e.End)); },
            e => { Assert.Equal("S2", e.SegmentLabel); Assert.Equal("BB", e.Quote); Assert.Equal((12, 14), (e.Start, e.End)); });
    }

    [Fact]
    public async Task Selection_across_three_segments_creates_three_entries()
    {
        var w = _db.SeedWorkspace();
        var dto = Http.Ok<ActionItemDto>(await Create(w.Id, Req(start: 0, end: 34)));
        Assert.Equal(["S1", "S2", "S3"], dto.Evidence.Select(e => e.SegmentLabel));
    }

    [Fact]
    public async Task Selection_covering_only_the_gap_creates_a_note_with_no_evidence()
    {
        var w = _db.SeedWorkspace();
        var dto = Http.Ok<ActionItemDto>(await Create(w.Id, Req(start: 10, end: 12)));
        Assert.Empty(dto.Evidence);
    }

    [Theory]
    [InlineData(-1, 5)]     // negative start
    [InlineData(5, 5)]      // empty
    [InlineData(6, 5)]      // reversed
    [InlineData(0, 35)]     // past the end of the source (length 34)
    public async Task Selection_outside_the_source_is_rejected(int start, int end)
    {
        var w = _db.SeedWorkspace();
        Assert.IsType<BadRequestObjectResult>(await Create(w.Id, Req(start: start, end: end)));
        await using var db = _db.NewContext();
        Assert.Empty(db.ActionItems);
    }

    [Fact]
    public async Task Selection_to_the_very_end_of_the_source_is_accepted()
    {
        var w = _db.SeedWorkspace();
        var dto = Http.Ok<ActionItemDto>(await Create(w.Id, Req(start: 30, end: 34)));
        Assert.Equal("CCCC", Assert.Single(dto.Evidence).Quote);
    }

    [Fact]
    public async Task Only_one_end_of_a_selection_is_rejected()
    {
        var w = _db.SeedWorkspace();
        Assert.IsType<BadRequestObjectResult>(await Create(w.Id, Req(start: 3)));
        Assert.IsType<BadRequestObjectResult>(await Create(w.Id, Req(end: 3)));
    }
}
