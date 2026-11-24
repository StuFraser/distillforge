using DistillForge.Api.Functions;
using DistillForge.Core.Integrations;
using DistillForge.Data.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DistillForge.Api.Tests;

public class IntegrationTests : IDisposable
{
    private readonly TestDb _db = new();
    public void Dispose() => _db.Dispose();
    private static readonly CancellationToken Ct = CancellationToken.None;
    private static SecretProtector Secrets() => new(Convert.ToBase64String(new byte[32]));

    private static IntegrationRequest GitHub(string name = "Repo", string? token = "ghp_secret") =>
        new(IntegrationPlatform.GitHub, name, new() { ["owner"] = "me", ["repo"] = "r", ["token"] = token });

    private async Task<IntegrationDto> Create(Guid? user = null, string name = "Repo")
    {
        await using var db = _db.NewContext(user);
        var result = await new IntegrationFunctions(db, Secrets()).Create(Http.Json(GitHub(name)), Ct);
        return Assert.IsType<IntegrationDto>(Assert.IsType<CreatedResult>(result).Value);
    }

    [Fact]
    public async Task Create_stores_secret_encrypted_and_never_returns_it()
    {
        var dto = await Create();
        Assert.Equal(["token"], dto.SecretsSet);
        Assert.Equal("me", dto.Settings["owner"]);
        Assert.DoesNotContain("token", dto.Settings.Keys);

        await using var db = _db.NewContext();
        var row = await db.Integrations.SingleAsync();
        Assert.DoesNotContain("ghp_secret", row.SettingsJson);
        Assert.DoesNotContain("ghp_secret", row.SecretsEncrypted!);
    }

    [Fact]
    public async Task Update_without_token_keeps_it_and_empty_token_is_refused_for_required_secret()
    {
        var dto = await Create();
        await using var db = _db.NewContext();
        var fns = new IntegrationFunctions(db, Secrets());

        var keep = GitHub("Renamed", token: null);
        Assert.IsType<OkObjectResult>(await fns.Update(Http.Json(keep), dto.Id, Ct));
        Assert.Equal(["token"], (await db.Integrations.AsNoTracking().SingleAsync()).SecretKeys);

        Assert.IsType<BadRequestObjectResult>(await fns.Update(Http.Json(GitHub("Renamed", token: "")), dto.Id, Ct));
    }

    [Fact]
    public async Task Update_cannot_change_platform_and_duplicate_names_conflict()
    {
        var a = await Create(name: "A");
        await Create(name: "B");
        await using var db = _db.NewContext();
        var fns = new IntegrationFunctions(db, Secrets());

        var other = new IntegrationRequest(IntegrationPlatform.Trello, "A", new() { ["apiKey"] = "k", ["token"] = "t", ["listId"] = "l" });
        Assert.IsType<BadRequestObjectResult>(await fns.Update(Http.Json(other), a.Id, Ct));
        Assert.IsType<ConflictObjectResult>(await fns.Update(Http.Json(GitHub("b")), a.Id, Ct));
    }

    [Fact]
    public async Task Another_user_cannot_see_update_or_delete_a_connection()
    {
        var dto = await Create();
        await using var asB = _db.NewContext(TestDb.UserB);
        var fns = new IntegrationFunctions(asB, Secrets());

        Assert.Empty(Assert.IsAssignableFrom<IEnumerable<IntegrationDto>>(Assert.IsType<OkObjectResult>(await fns.List(Http.Empty(), Ct)).Value));
        Assert.IsType<NotFoundResult>(await fns.Update(Http.Json(GitHub()), dto.Id, Ct));
        Assert.IsType<NotFoundResult>(await fns.Delete(Http.Empty(), dto.Id, Ct));

        await using var asA = _db.NewContext();
        Assert.Equal(1, await asA.Integrations.CountAsync());
    }

    [Fact]
    public async Task Same_name_is_allowed_for_different_users_and_delete_works()
    {
        var a = await Create(name: "Shared");
        await Create(TestDb.UserB, "Shared");

        await using var db = _db.NewContext();
        Assert.IsType<NoContentResult>(await new IntegrationFunctions(db, Secrets()).Delete(Http.Empty(), a.Id, Ct));
        Assert.Equal(0, await db.Integrations.CountAsync());
    }
}
