using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DistillForge.Api.Export;
using DistillForge.Api.Functions;
using DistillForge.Core.Export;
using DistillForge.Core.Integrations;
using DistillForge.Data.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DistillForge.Api.Tests;

internal sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

internal static class TrelloFakes
{
    public static readonly DateTimeOffset Now = new(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);

    public static HttpResponseMessage Json(HttpStatusCode code, string json) =>
        new(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    public static IOptions<TrelloOptions> Options(string? clientId = "CLIENT", string? secret = null, string? tokenUrl = null, string? apiBase = null) =>
        Microsoft.Extensions.Options.Options.Create(new TrelloOptions { ClientId = clientId, ClientSecret = secret, TokenUrl = tokenUrl, ApiBaseUrl = apiBase });

    public static TrelloOAuthClient OAuth(HttpMessageHandler h, IOptions<TrelloOptions>? o = null) => new(new Factory(h), o ?? Options(), new FixedClock(Now));

    public static TrelloTarget Target(HttpMessageHandler h, IOptions<TrelloOptions>? o = null) =>
        new(new Factory(h), o ?? Options(), OAuth(h, o), new FixedClock(Now));

    public const string TokenJson = """{"access_token":"AT1","refresh_token":"RT1","expires_in":3600,"scope":"read:board:trello"}""";
}

public class TrelloOAuthClientTests
{
    [Fact]
    public async Task Exchange_posts_the_code_with_pkce_and_no_secret_for_a_public_client()
    {
        var h = new FakeHandler((_, _) => TrelloFakes.Json(HttpStatusCode.OK, TrelloFakes.TokenJson));
        var t = await TrelloFakes.OAuth(h).ExchangeCodeAsync("CODE", "VERIFIER", "http://localhost:5189/trello-callback.html", default);

        Assert.Equal(new TrelloTokens("AT1", "RT1", TrelloFakes.Now.AddHours(1)), t);
        var (request, body) = h.Calls.Single();
        Assert.Equal("https://auth.atlassian.com/oauth/token", request.RequestUri!.AbsoluteUri);
        var json = JsonNode.Parse(body!)!;
        Assert.Equal("authorization_code", json["grant_type"]!.GetValue<string>());
        Assert.Equal("CLIENT", json["client_id"]!.GetValue<string>());
        Assert.Equal("CODE", json["code"]!.GetValue<string>());
        Assert.Equal("VERIFIER", json["code_verifier"]!.GetValue<string>());
        Assert.Equal("http://localhost:5189/trello-callback.html", json["redirect_uri"]!.GetValue<string>());
        Assert.Null(json["client_secret"]);
    }

    [Fact]
    public async Task A_configured_secret_is_sent_and_the_token_url_can_be_overridden()
    {
        var h = new FakeHandler((_, _) => TrelloFakes.Json(HttpStatusCode.OK, TrelloFakes.TokenJson));
        await TrelloFakes.OAuth(h, TrelloFakes.Options(secret: "S3CRET", tokenUrl: "https://auth.atlassian.com/authorize/oauth/token")).ExchangeCodeAsync("C", "V", "http://x/trello-callback.html", default);
        var (request, body) = h.Calls.Single();
        Assert.Equal("https://auth.atlassian.com/authorize/oauth/token", request.RequestUri!.AbsoluteUri);
        Assert.Equal("S3CRET", JsonNode.Parse(body!)!["client_secret"]!.GetValue<string>());
    }

    [Fact]
    public async Task Refresh_uses_the_refresh_grant_and_takes_the_rotated_token()
    {
        var h = new FakeHandler((_, _) => TrelloFakes.Json(HttpStatusCode.OK, """{"access_token":"AT2","refresh_token":"RT2","expires_in":3600}"""));
        var t = await TrelloFakes.OAuth(h).RefreshAsync("RT1", default);
        Assert.Equal(("AT2", "RT2"), (t.AccessToken, t.RefreshToken));
        var json = JsonNode.Parse(h.Calls.Single().Body!)!;
        Assert.Equal("refresh_token", json["grant_type"]!.GetValue<string>());
        Assert.Equal("RT1", json["refresh_token"]!.GetValue<string>());
    }

    [Fact]
    public async Task Refresh_keeps_the_old_refresh_token_if_none_is_returned_but_a_first_exchange_needs_one()
    {
        var noRefresh = new FakeHandler((_, _) => TrelloFakes.Json(HttpStatusCode.OK, """{"access_token":"AT2","expires_in":3600}"""));
        Assert.Equal("RT1", (await TrelloFakes.OAuth(noRefresh).RefreshAsync("RT1", default)).RefreshToken);
        var ex = await Assert.ThrowsAsync<TrelloOAuthException>(() => TrelloFakes.OAuth(noRefresh).ExchangeCodeAsync("C", "V", "http://x/trello-callback.html", default));
        Assert.Contains("offline_access", ex.Message);
    }

    [Fact]
    public async Task Failures_become_readable_messages()
    {
        var used = new FakeHandler((_, _) => TrelloFakes.Json(HttpStatusCode.BadRequest, """{"error":"invalid_grant","error_description":"Unknown or invalid refresh token."}"""));
        Assert.Contains("expired or was already used", (await Assert.ThrowsAsync<TrelloOAuthException>(() => TrelloFakes.OAuth(used).RefreshAsync("x", default))).Message);

        var other = new FakeHandler((_, _) => TrelloFakes.Json(HttpStatusCode.Unauthorized, """{"error":"access_denied","error_description":"bad client"}"""));
        Assert.Contains("bad client", (await Assert.ThrowsAsync<TrelloOAuthException>(() => TrelloFakes.OAuth(other).RefreshAsync("x", default))).Message);

        var down = new FakeHandler((_, _) => throw new HttpRequestException("no route"));
        Assert.Contains("no route", (await Assert.ThrowsAsync<TrelloOAuthException>(() => TrelloFakes.OAuth(down).RefreshAsync("x", default))).Message);

        var h = new FakeHandler((_, _) => TrelloFakes.Json(HttpStatusCode.OK, "{}"));
        await Assert.ThrowsAsync<TrelloOAuthException>(() => TrelloFakes.OAuth(h, TrelloFakes.Options(clientId: null)).RefreshAsync("x", default));
        Assert.Empty(h.Calls);
    }
}

public class TrelloTargetTests
{
    private const string Board1 = "5f1c0000000000000000b001", Board2 = "5f1c0000000000000000b002";
    private const string ListA = "5f1c0000000000000000a001", ListB = "5f1c0000000000000000a002";

    private static HttpResponseMessage Json(HttpStatusCode code, string json) => TrelloFakes.Json(code, json);

    private static TargetConnection Conn(DateTimeOffset? expires = null) => new(new Dictionary<string, string>(), new Dictionary<string, string>
    {
        ["accessToken"] = "AT/1", ["refreshToken"] = "RT1", ["expiresAt"] = (expires ?? TrelloFakes.Now.AddHours(1)).ToString("O"),
    });

    private static readonly ExportItem Item = new(Guid.NewGuid(), "  Send   budget ", "Needs sign-off", "Sam", "Fri", "we must send it", "S2", "Finance", "Board call");
    private static PushChoices Choices(string list = ListA) => new(list, Board1, null);

    private static HttpResponseMessage Route(HttpRequestMessage r) =>
        r.RequestUri!.AbsolutePath.EndsWith("/members/me/boards")
            ? Json(HttpStatusCode.OK, $$"""[{"id":"{{Board2}}","name":"Beta"},{"id":"{{Board1}}","name":"Alpha"},{"id":"nope","name":"Bad id"}]""")
            : Json(HttpStatusCode.OK, $$"""[{"id":"{{ListA}}","name":"To do"},{"id":"{{ListB}}","name":"Doing"}]""");

    [Fact]
    public async Task Options_list_boards_then_the_lists_of_the_first_board()
    {
        var h = new FakeHandler((r, _) => Route(r));
        var o = await TrelloFakes.Target(h).GetOptionsAsync(Conn(), null, default);

        Assert.Equal(["Beta", "Alpha"], o.Scopes!.Select(x => x.Label));      // Trello's order; ids are what's sent
        Assert.DoesNotContain(o.Scopes!, x => x.Value == "nope");              // malformed ids are dropped
        Assert.Equal(Board2, o.Scope);
        Assert.Equal(["To do", "Doing"], o.ItemTypes.Select(x => x.Label));
        Assert.Equal(ListA, o.DefaultItemType);
        Assert.Equal("Board", o.ScopeLabel);
        Assert.Equal("Add to list", o.ItemTypeLabel);
        Assert.False(o.HasAreaPath);
        Assert.Equal($"https://api.trello.com/1/boards/{Board2}/lists?filter=open&fields=name", h.Calls[1].Request.RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task The_access_token_goes_in_a_bearer_header_not_the_url_and_the_api_host_is_configurable()
    {
        var h = new FakeHandler((r, _) => Route(r));
        await TrelloFakes.Target(h, TrelloFakes.Options(apiBase: "https://trello.com/1")).GetOptionsAsync(Conn(), Board1, default);
        var request = h.Calls.Single().Request;
        Assert.Equal("Bearer AT/1", request.Headers.Authorization!.ToString());
        Assert.StartsWith("https://trello.com/1/boards/", request.RequestUri!.AbsoluteUri);
        Assert.DoesNotContain("AT", request.RequestUri.Query);
    }

    [Fact]
    public async Task A_board_id_from_the_client_must_look_like_a_trello_id()
    {
        var h = new FakeHandler((_, _) => Json(HttpStatusCode.OK, "[]"));
        var o = await TrelloFakes.Target(h).GetOptionsAsync(Conn(), "../members/me/tokens", default);
        Assert.NotNull(o.Error);
        Assert.Empty(h.Calls);
    }

    [Fact]
    public async Task Push_creates_a_card_on_the_chosen_list_with_a_form_body()
    {
        var h = new FakeHandler((_, _) => Json(HttpStatusCode.OK, """{"id":"5f1c0000000000000000c001","idShort":7,"shortUrl":"https://trello.com/c/abc"}"""));
        var o = await TrelloFakes.Target(h).PushAsync(Conn(), Item, Choices(ListB), default);

        Assert.True(o.Ok);
        Assert.Equal("7", o.ExternalId);
        Assert.Equal("https://trello.com/c/abc", o.Url);
        var (request, body) = h.Calls.Single();
        Assert.Equal("https://api.trello.com/1/cards", request.RequestUri!.AbsoluteUri);
        var form = System.Web.HttpUtility.ParseQueryString(body!);
        Assert.Equal(ListB, form["idList"]);
        Assert.Equal("Send budget", form["name"]);
        Assert.Equal("bottom", form["pos"]);
        Assert.Contains("**Owner:** Sam", form["desc"]);
        Assert.Contains("**Category:** Finance", form["desc"]);
        Assert.Contains("> “we must send it”", form["desc"]);
        Assert.Contains("_Source: S2_", form["desc"]);
    }

    [Fact]
    public async Task Push_without_a_valid_list_fails_the_note_without_calling_out()
    {
        var h = new FakeHandler((_, _) => Json(HttpStatusCode.OK, "{}"));
        var o = await TrelloFakes.Target(h).PushAsync(Conn(), Item, new("Task", Board1, null), default);
        Assert.False(o.Ok);
        Assert.Empty(h.Calls);
    }

    [Fact]
    public async Task Rejected_credentials_throw_and_a_note_level_refusal_does_not()
    {
        var bad = new FakeHandler((_, _) => new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("invalid token") });
        var thrown = await Assert.ThrowsAsync<ExportAuthException>(() => TrelloFakes.Target(bad).PushAsync(Conn(), Item, Choices(), default));
        Assert.Contains("invalid token", thrown.Message);   // shown on purpose, to diagnose host/scope problems
        Assert.Contains("Reconnect", (await TrelloFakes.Target(bad).GetOptionsAsync(Conn(), null, default)).Error);

        var refused = new FakeHandler((_, _) => new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("invalid value for idList") });
        var o = await TrelloFakes.Target(refused).PushAsync(Conn(), Item, Choices(), default);
        Assert.False(o.Ok);
        Assert.Contains("invalid value for idList", o.Error);
    }

    [Fact]
    public async Task Fresh_tokens_are_left_alone_and_expiring_ones_are_refreshed_and_rotated()
    {
        var h = new FakeHandler((_, _) => Json(HttpStatusCode.OK, """{"access_token":"AT2","refresh_token":"RT2","expires_in":3600}"""));
        var target = TrelloFakes.Target(h);

        Assert.Null(await target.RefreshSecretsAsync(Conn(TrelloFakes.Now.AddMinutes(30)), default));
        Assert.Empty(h.Calls);

        var renewed = (await target.RefreshSecretsAsync(Conn(TrelloFakes.Now.AddSeconds(30)), default))!;   // inside the 2-minute window
        Assert.Equal("AT2", renewed["accessToken"]);
        Assert.Equal("RT2", renewed["refreshToken"]);
        Assert.Equal(TrelloFakes.Now.AddHours(1), DateTimeOffset.Parse(renewed["expiresAt"], System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal("RT1", JsonNode.Parse(h.Calls.Single().Body!)!["refresh_token"]!.GetValue<string>());

        Assert.NotNull(await target.RefreshSecretsAsync(Conn(TrelloFakes.Now.AddHours(-1)), default));   // already expired
    }

    [Fact]
    public async Task Old_style_connections_and_unrenewable_ones_ask_the_user_to_reconnect()
    {
        var h = new FakeHandler((_, _) => Json(HttpStatusCode.BadRequest, """{"error":"invalid_grant"}"""));
        var target = TrelloFakes.Target(h);

        var legacy = new TargetConnection(new Dictionary<string, string>(), new Dictionary<string, string> { ["token"] = "old", ["apiKey"] = "old" });
        Assert.Contains("Reconnect", (await Assert.ThrowsAsync<ExportAuthException>(() => target.RefreshSecretsAsync(legacy, default))).Message);

        var revoked = await Assert.ThrowsAsync<ExportAuthException>(() => target.RefreshSecretsAsync(Conn(TrelloFakes.Now.AddHours(-1)), default));
        Assert.Contains("Reconnect", revoked.Message);
    }
}

public class TrelloConnectFunctionsTests : IDisposable
{
    private readonly TestDb _db = new();
    public void Dispose() => _db.Dispose();
    private static readonly CancellationToken Ct = CancellationToken.None;
    private static SecretProtector Secrets() => new(Convert.ToBase64String(new byte[32]));
    private const string Redirect = "http://localhost:5189/trello-callback.html";
    private static readonly string Verifier = new('v', 64);

    private static TrelloConnectRequest Req(string name = "My Trello", Guid? id = null, string redirect = Redirect) => new(name, "CODE", Verifier, redirect, id);

    private TrelloConnectFunctions Fns(DistillForge.Data.DistillForgeDbContext db, FakeHandler h, IOptions<TrelloOptions>? o = null)
    {
        o ??= TrelloFakes.Options();
        return new(db, Secrets(), TrelloFakes.OAuth(h, o), o);
    }

    private static FakeHandler Ok() => new((_, _) => TrelloFakes.Json(HttpStatusCode.OK, TrelloFakes.TokenJson));

    [Fact]
    public void Connect_info_reports_the_client_id_only_when_one_is_set()
    {
        TrelloConnectInfoDto Info(string? id) => Assert.IsType<TrelloConnectInfoDto>(Assert.IsType<OkObjectResult>(
            Fns(_db.NewContext(), Ok(), TrelloFakes.Options(clientId: id)).Info(Http.Empty())).Value);
        Assert.Equal(new TrelloConnectInfoDto(true, "abc"), Info(" abc "));
        Assert.Equal(new TrelloConnectInfoDto(false, null), Info(null));
        Assert.Equal(new TrelloConnectInfoDto(false, null), Info("  "));
    }

    [Fact]
    public async Task Connect_stores_the_tokens_encrypted_and_never_returns_them()
    {
        await using var db = _db.NewContext();
        var dto = Assert.IsType<IntegrationDto>(Assert.IsType<CreatedResult>(await Fns(db, Ok()).Connect(Http.Json(Req()), Ct)).Value);

        Assert.Equal(IntegrationPlatform.Trello, dto.Platform);
        Assert.Equal(["accessToken", "expiresAt", "refreshToken"], dto.SecretsSet);
        Assert.Empty(dto.Settings);
        var row = await db.Integrations.AsNoTracking().SingleAsync();
        Assert.DoesNotContain("AT1", row.SecretsEncrypted!);
        Assert.Equal("RT1", JsonSerializer.Deserialize<Dictionary<string, string>>(Secrets().Unprotect(row.SecretsEncrypted!))!["refreshToken"]);
    }

    [Fact]
    public async Task Reconnect_replaces_the_tokens_on_the_same_connection()
    {
        await using var db = _db.NewContext();
        var first = Assert.IsType<IntegrationDto>(Assert.IsType<CreatedResult>(await Fns(db, Ok()).Connect(Http.Json(Req()), Ct)).Value);

        var again = new FakeHandler((_, _) => TrelloFakes.Json(HttpStatusCode.OK, """{"access_token":"AT9","refresh_token":"RT9","expires_in":3600}"""));
        Assert.IsType<OkObjectResult>(await Fns(db, again).Connect(Http.Json(Req("Renamed", first.Id)), Ct));

        var row = await db.Integrations.AsNoTracking().SingleAsync();
        Assert.Equal(first.Id, row.Id);
        Assert.Equal("Renamed", row.Name);
        Assert.Equal("RT9", JsonSerializer.Deserialize<Dictionary<string, string>>(Secrets().Unprotect(row.SecretsEncrypted!))!["refreshToken"]);
    }

    [Fact]
    public async Task Duplicate_names_conflict_and_another_users_connection_cannot_be_reconnected()
    {
        await using var db = _db.NewContext();
        Assert.IsType<CreatedResult>(await Fns(db, Ok()).Connect(Http.Json(Req("Dup")), Ct));
        var h = Ok();
        Assert.IsType<ConflictObjectResult>(await Fns(db, h).Connect(Http.Json(Req("dup")), Ct));
        Assert.Empty(h.Calls);

        var id = (await db.Integrations.SingleAsync()).Id;
        await using var asB = _db.NewContext(TestDb.UserB);
        Assert.IsType<NotFoundResult>(await Fns(asB, Ok()).Connect(Http.Json(Req("Other", id)), Ct));
    }

    [Theory]
    [InlineData("https://evil.example/steal")]
    [InlineData("http://localhost:5189/trello-callback.html?x=1")]
    [InlineData("javascript:alert(1)")]
    public async Task Only_the_apps_callback_page_is_accepted_as_the_redirect(string redirect)
    {
        var h = Ok();
        await using var db = _db.NewContext();
        Assert.IsType<BadRequestObjectResult>(await Fns(db, h).Connect(Http.Json(Req(redirect: redirect)), Ct));
        Assert.Empty(h.Calls);
    }

    [Fact]
    public async Task Exchange_failures_and_missing_setup_are_reported_and_nothing_is_saved()
    {
        await using var db = _db.NewContext();
        var denied = new FakeHandler((_, _) => TrelloFakes.Json(HttpStatusCode.BadRequest, """{"error":"invalid_grant"}"""));
        var failed = Assert.IsType<BadRequestObjectResult>(await Fns(db, denied).Connect(Http.Json(Req()), Ct));
        Assert.Contains("already used", failed.Value!.ToString());

        var unset = Assert.IsType<BadRequestObjectResult>(await Fns(db, Ok(), TrelloFakes.Options(clientId: null)).Connect(Http.Json(Req()), Ct));
        Assert.Contains("Trello__ClientId", unset.Value!.ToString());

        Assert.Empty(await db.Integrations.ToListAsync());
    }

    [Fact]
    public async Task The_generic_create_endpoint_refuses_trello_because_it_needs_the_sign_in()
    {
        await using var db = _db.NewContext();
        var result = await new IntegrationFunctions(db, Secrets()).Create(Http.Json(new IntegrationRequest(IntegrationPlatform.Trello, "x", new())), Ct);
        Assert.Contains("Connect with Trello", Assert.IsType<BadRequestObjectResult>(result).Value!.ToString());
    }
}
