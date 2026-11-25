using DistillForge.Api.Functions;
using DistillForge.Api.Services;
using DistillForge.Core.Contracts;
using DistillForge.Data.Entities;
using DistillForge.Data.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DistillForge.Api.Tests;

/// <summary>User B must not be able to see, change or delete anything that belongs to user A, and with nobody signed in nothing is visible.</summary>
public class CrossUserTests : IDisposable
{
    private static readonly Guid B = TestDb.UserB;
    private readonly TestDb _db = new();
    public void Dispose() => _db.Dispose();

    private static readonly CancellationToken Ct = CancellationToken.None;

    private static IEnumerable<T> Items<T>(IActionResult result) =>
        Assert.IsAssignableFrom<IEnumerable<T>>(Assert.IsType<OkObjectResult>(result).Value);

    private static SecretProtector Secrets() => new(Convert.ToBase64String(new byte[32]));

    private WorkspaceFunctions WorkspaceFns(DistillForge.Data.DistillForgeDbContext db) => new(db, new WorkspaceService(db), new SourceFileExtractor());

    [Fact]
    public async Task Workspace_list_only_shows_own_workspaces()
    {
        var a = _db.SeedWorkspace();
        var b = _db.SeedWorkspace(owner: B);

        // (The List endpoint orders by a DateTimeOffset, which SQLite can't do, so check the filtered set it queries.)
        await using var asA = _db.NewContext();
        await using var asB = _db.NewContext(B);

        Assert.Equal([a.Id], await asA.Workspaces.Select(w => w.Id).ToListAsync());
        Assert.Equal([b.Id], await asB.Workspaces.Select(w => w.Id).ToListAsync());
    }

    [Fact]
    public async Task Another_users_workspace_is_404_to_read_archive_and_replace()
    {
        var w = _db.SeedWorkspace();
        await using var db = _db.NewContext(B);
        var fns = WorkspaceFns(db);

        Assert.IsType<NotFoundResult>(await fns.Get(Http.Empty(), w.Id, Ct));
        Assert.IsType<NotFoundResult>(await fns.Archive(Http.Empty(), w.Id, Ct));
        Assert.IsType<NotFoundResult>(await fns.ReplaceSource(Http.Json(new ReplaceSourceRequest("new text")), w.Id, Ct));

        await using var asA = _db.NewContext();
        var still = await asA.Workspaces.SingleAsync();
        Assert.False(still.IsArchived);
        Assert.Equal(3, await asA.Segments.CountAsync());
    }

    [Fact]
    public async Task Replace_source_works_for_the_owner_and_wipes_their_notes_only()
    {
        var mine = _db.SeedWorkspace();
        var theirs = _db.SeedWorkspace(owner: B);
        _db.SeedNote(mine.Id);
        _db.SeedNote(theirs.Id);

        await using (var db = _db.NewContext())
            Assert.IsType<OkObjectResult>(await WorkspaceFns(db).ReplaceSource(Http.Json(new ReplaceSourceRequest("Fresh text")), mine.Id, Ct));

        await using var asB = _db.NewContext(B);
        Assert.Single(await asB.ActionItems.ToListAsync());
        await using var asA = _db.NewContext();
        Assert.Empty(await asA.ActionItems.ToListAsync());
    }

    [Fact]
    public async Task Another_users_lists_are_empty_not_leaky()
    {
        var w = _db.SeedWorkspace();
        _db.SeedNote(w.Id);
        _db.SeedCategory(w.Id);
        await using var db = _db.NewContext(B);

        Assert.Empty(Http.Ok<List<ActionItemDto>>(await WorkspaceFns(db).ListActions(Http.Empty(), w.Id, Ct)));
        Assert.Empty(Items<CategoryDto>(await new CategoryFunctions(db).List(Http.Empty(), w.Id, Ct)));
    }

    [Fact]
    public async Task Another_users_notes_cannot_be_edited_deleted_created_in_merged_or_moved()
    {
        var w = _db.SeedWorkspace();
        var cat = _db.SeedCategory(w.Id);
        var n1 = _db.SeedNote(w.Id, "one", 0);
        var n2 = _db.SeedNote(w.Id, "two", 1);
        await using var db = _db.NewContext(B);
        var notes = new ActionItemFunctions(db);

        Assert.IsType<NotFoundResult>(await notes.Update(Http.Json(new UpdateActionItemRequest("hacked", null, null, null, null)), n1.Id, Ct));
        Assert.IsType<NotFoundResult>(await notes.Delete(Http.Empty(), n1.Id, Ct));
        Assert.IsType<NotFoundResult>(await notes.Create(Http.Json(new CreateActionItemRequest("planted", null, null, null, null, null)), w.Id, Ct));
        Assert.IsType<NotFoundResult>(await new NoteMergeFunctions(db).Merge(Http.Json(new MergeActionItemRequest(n2.Id)), n1.Id, Ct));
        Assert.IsType<NotFoundResult>(await new CategoryFunctions(db).Delete(Http.Empty(), cat.Id, Ct));
        Assert.IsType<NotFoundResult>(await new CategoryFunctions(db).Create(Http.Json(new CategoryRequest("planted", null)), w.Id, Ct));

        await using var asA = _db.NewContext();
        Assert.Equal(["one", "two"], (await asA.ActionItems.OrderBy(a => a.SortOrder).ToListAsync()).Select(a => a.Title));
        Assert.Single(await asA.Categories.ToListAsync());
    }

    [Fact]
    public async Task Another_users_workspace_cannot_be_extracted()
    {
        var w = _db.SeedWorkspace();
        await using var db = _db.NewContext(B);

        var started = await new ExtractionFunctions(db, null!).Start(Http.Empty(), w.Id, Ct);

        Assert.IsType<NotFoundResult>(started.Result);
        Assert.Null(started.RunId);
        await using var asA = _db.NewContext();
        Assert.Empty(await asA.ExtractionRuns.ToListAsync());
    }

    [Fact]
    public async Task Another_users_run_is_404_to_read_and_cancel()
    {
        var w = _db.SeedWorkspace();
        var run = new ExtractionRun { Id = Guid.NewGuid(), WorkspaceId = w.Id, Status = ExtractionRunStatus.Queued, CreatedAt = DateTimeOffset.UtcNow };
        await using (var seed = _db.NewContext()) { seed.ExtractionRuns.Add(run); await seed.SaveChangesAsync(); }

        await using var db = _db.NewContext(B);
        var fns = new ExtractionFunctions(db, null!);
        Assert.IsType<NotFoundResult>(await fns.GetRun(Http.Empty(), run.Id, Ct));
        Assert.IsType<NotFoundResult>(await fns.Cancel(Http.Empty(), run.Id, Ct));

        await using var asA = _db.NewContext();
        Assert.Equal(ExtractionRunStatus.Queued, (await asA.ExtractionRuns.SingleAsync()).Status);
    }

    [Fact]
    public async Task AI_providers_and_models_are_per_user()
    {
        await using (var a = _db.NewContext())
        {
            var created = await new AiProviderFunctions(a, Secrets(), null!).Create(Http.Json(new AiProviderRequest("Mine", "http://x/v1", "secret")), Ct);
            Assert.IsType<CreatedResult>(created);
        }
        var provider = await _db.NewContext().AiProviders.SingleAsync();
        Assert.Equal(TestDb.UserA, provider.OwnerId);

        await using var db = _db.NewContext(B);
        var fns = new AiProviderFunctions(db, Secrets(), null!);
        Assert.Empty(Items<AiProviderDto>(await fns.List(Http.Empty(), Ct)));
        Assert.IsType<NotFoundResult>(await fns.Delete(Http.Empty(), provider.Id, Ct));
        Assert.IsType<NotFoundResult>(await fns.CreateModel(Http.Json(new AiModelRequest("m", null, 8192, 2048, ResponseFormatMode.JsonSchema, false)), provider.Id, Ct));
        Assert.IsType<NotFoundResult>(await fns.Update(Http.Json(new AiProviderRequest("Taken", "http://evil/v1", "k")), provider.Id, Ct));
        Assert.Empty(Items<AiModelChoiceDto>(await fns.ListModels(Http.Empty(), Ct)));

        // B may reuse A's provider name: names are unique per user.
        Assert.IsType<CreatedResult>(await fns.Create(Http.Json(new AiProviderRequest("Mine", "http://y/v1", null)), Ct));
        await using var asA = _db.NewContext();
        Assert.Equal("http://x/v1", (await asA.AiProviders.SingleAsync()).BaseUrl);
    }

    [Fact]
    public async Task Each_user_can_have_their_own_default_model()
    {
        foreach (var user in new[] { TestDb.UserA, B })
        {
            await using var db = _db.NewContext(user);
            var fns = new AiProviderFunctions(db, Secrets(), null!);
            var p = Assert.IsType<CreatedResult>(await fns.Create(Http.Json(new AiProviderRequest("P", "http://x/v1", null)), Ct));
            var id = ((AiProviderDto)p.Value!).Id;
            Assert.IsType<CreatedResult>(await fns.CreateModel(Http.Json(new AiModelRequest("m", null, 8192, 2048, ResponseFormatMode.JsonSchema, true)), id, Ct));
        }

        await using var all = _db.NewContext();
        Assert.Equal(1, await all.AiModels.CountAsync(m => m.IsDefault));
        await using var asB = _db.NewContext(B);
        Assert.Equal(1, await asB.AiModels.CountAsync(m => m.IsDefault));
    }

    [Fact]
    public async Task With_nobody_signed_in_nothing_is_visible()
    {
        var w = _db.SeedWorkspace();
        _db.SeedNote(w.Id);
        _db.SeedCategory(w.Id);

        await using var db = _db.NewContext(TestDb.NoUser);
        Assert.Empty(await db.Workspaces.ToListAsync());
        Assert.Empty(await db.Segments.ToListAsync());
        Assert.Empty(await db.ActionItems.ToListAsync());
        Assert.Empty(await db.Categories.ToListAsync());
        Assert.Empty(await db.AiProviders.ToListAsync());
    }

    [Fact]
    public async Task Ownerless_rows_are_invisible_even_to_a_signed_in_user()
    {
        await using (var db = _db.NewContext())
        {
            db.Workspaces.Add(new Workspace { Id = Guid.NewGuid(), OwnerId = TestDb.UserA, Title = "x", Code = DistillForge.Core.Codes.ShortCode.New(), SourceText = "x", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
            await db.Database.ExecuteSqlRawAsync("UPDATE Workspaces SET OwnerId = NULL");
        }

        await using var asA = _db.NewContext();
        Assert.Empty(await asA.Workspaces.ToListAsync());
        Assert.Single(await asA.Workspaces.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task New_workspaces_are_stamped_with_the_current_user_and_cannot_be_created_without_one()
    {
        await using (var db = _db.NewContext(B))
        {
            var w = await new WorkspaceService(db).CreateAsync("Some source text.", "t", null, Ct);
            Assert.Equal(B, w.OwnerId);
        }

        await using var nobody = _db.NewContext(TestDb.NoUser);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new WorkspaceService(nobody).CreateAsync("Some source text.", "t", null, Ct));
    }
}
