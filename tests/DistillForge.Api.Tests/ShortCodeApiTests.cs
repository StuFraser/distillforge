using DistillForge.Api.Functions;
using DistillForge.Api.Services;
using DistillForge.Core.Codes;
using DistillForge.Core.Contracts;
using DistillForge.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DistillForge.Api.Tests;

public class ShortCodeApiTests : IDisposable
{
    private readonly TestDb _db = new();
    public void Dispose() => _db.Dispose();

    private static WorkspaceFunctions Fns(DistillForgeDbContext db) => new(db, new WorkspaceService(db), new SourceFileExtractor());
    private Task<IActionResult> Lookup(string code, Guid? user = null)
    {
        var db = _db.NewContext(user);
        return Fns(db).Lookup(null!, code, CancellationToken.None);
    }

    [Fact]
    public async Task New_workspaces_get_a_code_and_it_is_in_the_dto()
    {
        await using var db = _db.NewContext();
        var w = await new WorkspaceService(db).CreateAsync("Some source text.", "Named", null, default);
        Assert.Equal(ShortCode.Length, w.Code.Length);
        Assert.Equal(ShortCode.ForWorkspace(w.Code), w.ToDto().Code);
    }

    [Fact]
    public async Task Lookup_finds_a_workspace_by_its_code_in_any_case()
    {
        var w = _db.SeedWorkspace();
        var hit = Http.Ok<LookupResultDto>(await Lookup(ShortCode.ForWorkspace(w.Code).ToLowerInvariant()));
        Assert.Equal(w.Id, hit.WorkspaceId);
        Assert.Null(hit.NoteId);
    }

    [Fact]
    public async Task Lookup_finds_a_note_and_its_workspace()
    {
        var w = _db.SeedWorkspace();
        var n = _db.SeedNote(w.Id);
        var hit = Http.Ok<LookupResultDto>(await Lookup(ShortCode.ForNote(n.Code)));
        Assert.Equal(w.Id, hit.WorkspaceId);
        Assert.Equal(n.Id, hit.NoteId);
    }

    [Fact]
    public async Task Lookup_never_reveals_another_users_workspace_or_note()
    {
        var w = _db.SeedWorkspace();
        var n = _db.SeedNote(w.Id);
        var other = Guid.NewGuid();
        Assert.IsType<NotFoundResult>(await Lookup(ShortCode.ForWorkspace(w.Code), other));
        Assert.IsType<NotFoundResult>(await Lookup(ShortCode.ForNote(n.Code), other));
    }

    [Fact]
    public async Task Lookup_rejects_text_that_is_not_a_code_and_a_workspace_code_is_not_a_note_code()
    {
        var w = _db.SeedWorkspace();
        Assert.IsType<BadRequestObjectResult>(await Lookup("hello"));
        Assert.IsType<NotFoundResult>(await Lookup(ShortCode.ForNote(w.Code)));
    }

    [Fact]
    public async Task Rename_changes_the_title_but_not_the_code()
    {
        var w = _db.SeedWorkspace();
        await using var db = _db.NewContext();
        var dto = Http.Ok<WorkspaceDto>(await Fns(db).Rename(Http.Json(new RenameWorkspaceRequest("  Q4   planning  ")), w.Id, CancellationToken.None));
        Assert.Equal("Q4 planning", dto.Title);
        Assert.Equal(ShortCode.ForWorkspace(w.Code), dto.Code);
    }

    [Fact]
    public async Task Rename_rejects_a_blank_name_and_other_users_workspaces()
    {
        var w = _db.SeedWorkspace();
        await using (var db = _db.NewContext())
            Assert.IsType<BadRequestObjectResult>(await Fns(db).Rename(Http.Json(new RenameWorkspaceRequest("   ")), w.Id, CancellationToken.None));
        await using (var db = _db.NewContext(Guid.NewGuid()))
            Assert.IsType<NotFoundResult>(await Fns(db).Rename(Http.Json(new RenameWorkspaceRequest("Mine now")), w.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Manually_added_notes_get_distinct_codes()
    {
        var w = _db.SeedWorkspace();
        var codes = new List<string>();
        for (var i = 0; i < 5; i++)
        {
            await using var db = _db.NewContext();
            var dto = Http.Ok<ActionItemDto>(await new ActionItemFunctions(db).Create(Http.Json(new CreateActionItemRequest($"n{i}", null, null, null, null, null, null, null)), w.Id, CancellationToken.None));
            codes.Add(dto.Code);
        }
        Assert.Equal(5, codes.Distinct().Count());
        Assert.All(codes, c => Assert.StartsWith("N-", c));
    }
}
