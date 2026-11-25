using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DistillForge.Api.Export;
using DistillForge.Api.Functions;
using DistillForge.Core.Export;
using DistillForge.Core.Integrations;
using DistillForge.Data.Entities;
using DistillForge.Data.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DistillForge.Api.Tests;

public class AzureDevOpsTargetTests
{
    private sealed class FakeHandler(Func<HttpRequestMessage, string?, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, string? Body)> Calls { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            Calls.Add((request, body));
            return respond(request, body);
        }
    }

    private sealed class Factory(HttpMessageHandler h) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(h, disposeHandler: false);
    }

    private static HttpResponseMessage Json(HttpStatusCode code, string json) =>
        new(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static TargetConnection Conn(string org = "https://dev.azure.com/acme", string project = "") => new(
        new Dictionary<string, string> { ["organizationUrl"] = org, ["project"] = project },
        new Dictionary<string, string> { ["pat"] = "secret-pat" });

    private static readonly ExportItem Item = new(Guid.NewGuid(), "  Send   the <b>budget</b> ", "Needs sign-off", "Sam", "Fri", "we must send it", "S2", "Finance; Q4", "Board <call>");

    private static PushChoices Choices(string type = "Task", string? scope = "My Project", string? area = null) => new(type, scope, area);

    [Fact]
    public async Task Options_lists_projects_then_the_types_of_the_chosen_one()
    {
        var h = new FakeHandler((r, _) => r.RequestUri!.AbsolutePath.EndsWith("/_apis/projects")
            ? Json(HttpStatusCode.OK, """{"value":[{"name":"Zeta"},{"name":"Alpha"}]}""")
            : Json(HttpStatusCode.OK, """{"value":[{"name":"Bug"},{"name":"Task"},{"name":"Old","isDisabled":true}]}"""));
        var o = await new AzureDevOpsTarget(new Factory(h)).GetOptionsAsync(Conn(), null, default);

        Assert.Equal(["Alpha", "Zeta"], o.Scopes);
        Assert.Equal("Alpha", o.Scope);
        Assert.Equal(["Bug", "Task"], o.ItemTypes);
        Assert.Equal("Task", o.DefaultItemType);
        Assert.Equal("Project", o.ScopeLabel);
        Assert.Equal("https://dev.azure.com/acme/_apis/projects?api-version=7.1&$top=500", h.Calls[0].Request.RequestUri!.AbsoluteUri);
        Assert.Equal("https://dev.azure.com/acme/Alpha/_apis/wit/workitemtypes?api-version=7.1", h.Calls[1].Request.RequestUri!.AbsoluteUri);
        Assert.Equal("Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(":secret-pat")), h.Calls[0].Request.Headers.Authorization!.ToString());
    }

    [Fact]
    public async Task The_saved_default_project_is_preselected_when_it_exists()
    {
        var h = new FakeHandler((r, _) => r.RequestUri!.AbsolutePath.EndsWith("/_apis/projects")
            ? Json(HttpStatusCode.OK, """{"value":[{"name":"Alpha"},{"name":"My Project"}]}""")
            : Json(HttpStatusCode.OK, """{"value":[{"name":"Task"}]}"""));
        var o = await new AzureDevOpsTarget(new Factory(h)).GetOptionsAsync(Conn(project: "My Project"), null, default);
        Assert.Equal("My Project", o.Scope);
        Assert.Contains("/My%20Project/_apis/wit/workitemtypes", h.Calls[1].Request.RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task Asking_for_a_scope_only_fetches_its_types()
    {
        var h = new FakeHandler((_, _) => Json(HttpStatusCode.OK, """{"value":[{"name":"Epic"}]}"""));
        var o = await new AzureDevOpsTarget(new Factory(h)).GetOptionsAsync(Conn(), "Other", default);
        Assert.Null(o.Scopes);
        Assert.Equal("Other", o.Scope);
        Assert.Single(h.Calls);
    }

    [Fact]
    public async Task A_token_that_cannot_list_projects_falls_back_to_the_saved_default()
    {
        var h = new FakeHandler((r, _) => r.RequestUri!.AbsolutePath.EndsWith("/_apis/projects")
            ? new HttpResponseMessage(HttpStatusCode.Forbidden)
            : Json(HttpStatusCode.OK, """{"value":[{"name":"Task"}]}"""));
        var o = await new AzureDevOpsTarget(new Factory(h)).GetOptionsAsync(Conn(project: "Mine"), null, default);
        Assert.Null(o.Error);
        Assert.Equal(["Mine"], o.Scopes);

        var noDefault = await new AzureDevOpsTarget(new Factory(h)).GetOptionsAsync(Conn(), null, default);
        Assert.Contains("Project and Team", noDefault.Error);
    }

    [Fact]
    public async Task Options_reports_a_rejected_token_as_an_error_not_an_exception()
    {
        var h = new FakeHandler((_, _) => new HttpResponseMessage(HttpStatusCode.NonAuthoritativeInformation) { Content = new StringContent("<html>sign in</html>") });
        var o = await new AzureDevOpsTarget(new Factory(h)).GetOptionsAsync(Conn(), null, default);
        Assert.Empty(o.ItemTypes);
        Assert.Contains("rejected the token", o.Error);
    }

    [Fact]
    public async Task Push_posts_a_json_patch_to_the_chosen_project_and_type_and_returns_the_link()
    {
        var h = new FakeHandler((_, _) => Json(HttpStatusCode.OK, """{"id":42,"_links":{"html":{"href":"https://dev.azure.com/acme/_workitems/edit/42"}}}"""));
        var outcome = await new AzureDevOpsTarget(new Factory(h)).PushAsync(Conn(), Item, Choices("Bug", "My Project", "My Project\\Team"), default);

        Assert.True(outcome.Ok);
        Assert.Equal("42", outcome.ExternalId);
        Assert.Equal("https://dev.azure.com/acme/_workitems/edit/42", outcome.Url);
        var (request, body) = h.Calls.Single();
        Assert.Equal("https://dev.azure.com/acme/My%20Project/_apis/wit/workitems/$Bug?api-version=7.1", request.RequestUri!.AbsoluteUri);
        Assert.Equal("application/json-patch+json", request.Content!.Headers.ContentType!.MediaType);

        var ops = JsonNode.Parse(body!)!.AsArray().ToDictionary(o => o!["path"]!.GetValue<string>(), o => o!["value"]!.GetValue<string>());
        Assert.Equal("Send the <b>budget</b>", ops["/fields/System.Title"]);
        Assert.Equal("My Project\\Team", ops["/fields/System.AreaPath"]);
        Assert.Equal("Finance  Q4", ops["/fields/System.Tags"]);   // ';' would split it into two tags
    }

    [Fact]
    public async Task A_project_name_cannot_escape_its_path_segment()
    {
        var h = new FakeHandler((_, _) => Json(HttpStatusCode.OK, """{"id":1}"""));
        await new AzureDevOpsTarget(new Factory(h)).PushAsync(Conn(), Item, Choices(scope: "../../x?y=1#z"), default);
        var uri = h.Calls.Single().Request.RequestUri!;
        Assert.Equal("dev.azure.com", uri.Host);
        Assert.StartsWith("/acme/..%2F..%2Fx%3Fy%3D1%23z/_apis/wit/workitems/", uri.AbsolutePath);
    }

    [Fact]
    public async Task Push_without_a_project_fails_the_note_without_calling_out()
    {
        var h = new FakeHandler((_, _) => Json(HttpStatusCode.OK, "{}"));
        var o = await new AzureDevOpsTarget(new Factory(h)).PushAsync(Conn(), Item, Choices(scope: " "), default);
        Assert.False(o.Ok);
        Assert.Empty(h.Calls);
    }

    [Fact]
    public void Description_is_html_encoded_and_includes_owner_due_and_source()
    {
        var html = AzureDevOpsTarget.BuildDescriptionHtml(Item);
        Assert.Contains("Needs sign-off", html);
        Assert.Contains("<b>Owner:</b> Sam", html);
        Assert.Contains("<b>Due:</b> Fri", html);
        Assert.Contains("“we must send it”", html);
        Assert.Contains("Source: S2", html);
        Assert.Contains("Board &lt;call&gt;", html);
        Assert.DoesNotContain("<call>", html);
    }

    [Fact]
    public async Task Auth_failure_on_push_throws_so_the_caller_can_stop()
    {
        var h = new FakeHandler((_, _) => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        await Assert.ThrowsAsync<ExportAuthException>(() => new AzureDevOpsTarget(new Factory(h)).PushAsync(Conn(), Item, Choices(), default));
    }

    [Fact]
    public async Task Note_specific_refusal_is_a_failed_outcome_with_the_servers_message()
    {
        var h = new FakeHandler((_, _) => Json(HttpStatusCode.BadRequest, """{"message":"TF401320: Rule Error for field Title."}"""));
        var o = await new AzureDevOpsTarget(new Factory(h)).PushAsync(Conn(), Item, Choices(), default);
        Assert.False(o.Ok);
        Assert.Contains("TF401320", o.Error);
    }

    [Theory]
    [InlineData("http://169.254.169.254")]
    [InlineData("https://internal.example.com/acme")]
    [InlineData("https://dev.azure.com.evil.example/acme")]
    public async Task Never_calls_a_host_that_isnt_azure_devops(string org)
    {
        var h = new FakeHandler((_, _) => Json(HttpStatusCode.OK, "{}"));
        var o = await new AzureDevOpsTarget(new Factory(h)).GetOptionsAsync(Conn(org), null, default);
        Assert.NotNull(o.Error);
        Assert.Empty(h.Calls);
        await Assert.ThrowsAsync<ExportAuthException>(() => new AzureDevOpsTarget(new Factory(h)).PushAsync(Conn(org), Item, Choices(), default));
        Assert.Empty(h.Calls);
    }
}

public class PushFunctionsTests : IDisposable
{
    private readonly TestDb _db = new();
    public void Dispose() => _db.Dispose();
    private static readonly CancellationToken Ct = CancellationToken.None;
    private static SecretProtector Secrets() => new(Convert.ToBase64String(new byte[32]));

    private sealed class FakeTarget : IExportTarget
    {
        public IntegrationPlatform Platform => IntegrationPlatform.AzureDevOps;
        public List<ExportItem> Pushed { get; } = [];
        public HashSet<string> FailTitles { get; } = [];
        public bool AuthFails { get; set; }
        public string? ScopeLabel => "Project";
        public List<PushChoices> Choices { get; } = [];
        public Task<PushOptionsDto> GetOptionsAsync(TargetConnection c, string? scope, CancellationToken ct) => Task.FromResult(new PushOptionsDto(["Task"], "Task", null));
        public Task<PushOutcome> PushAsync(TargetConnection c, ExportItem item, PushChoices choices, CancellationToken ct)
        {
            Choices.Add(choices);
            if (AuthFails) throw new ExportAuthException("bad token");
            if (FailTitles.Contains(item.Title)) return Task.FromResult(new PushOutcome(item.NoteId, false, null, null, "nope"));
            Pushed.Add(item);
            return Task.FromResult(new PushOutcome(item.NoteId, true, "100" + Pushed.Count, "https://x/" + Pushed.Count, null));
        }
    }

    private Guid SeedIntegration(Guid? owner = null, IntegrationPlatform platform = IntegrationPlatform.AzureDevOps)
    {
        using var db = _db.NewContext(owner);
        var i = new Integration { Id = Guid.NewGuid(), Platform = platform, Name = "ADO" + Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow, SettingsJson = "{}" };
        db.Integrations.Add(i);
        db.SaveChanges();
        return i.Id;
    }

    private PushFunctions Fns(DistillForge.Data.DistillForgeDbContext db, FakeTarget t) => new(db, Secrets(), [t]);

    private static PushRequest Req(Guid ws, IEnumerable<Guid> notes, bool repush = false, bool source = true, string? scope = "Proj", string? area = null) =>
        new(ws, notes.ToList(), "Task", source, repush, scope, area);

    private static List<PushOutcome> Outcomes(IActionResult r) => Assert.IsType<PushResponse>(Assert.IsType<OkObjectResult>(r).Value).Outcomes.ToList();

    [Fact]
    public async Task Pushes_notes_with_category_and_source_and_records_them()
    {
        var ws = _db.SeedWorkspace();
        var cat = _db.SeedCategory(ws.Id, "Finance");
        var seg = ws.Segments.First();
        var n = _db.SeedNote(ws.Id, "Send budget", categoryId: cat.Id, evidenceSegmentId: seg.Id);
        var integration = SeedIntegration();
        var target = new FakeTarget();

        await using var db = _db.NewContext();
        var outcomes = Outcomes(await Fns(db, target).Push(Http.Json(Req(ws.Id, [n.Id])), integration, Ct));

        Assert.True(outcomes.Single().Ok);
        var item = target.Pushed.Single();
        Assert.Equal("Finance", item.CategoryName);
        Assert.Equal("AAAA", item.SourceQuote);
        Assert.Equal(1, await db.NotePushes.CountAsync());

        var listed = Assert.IsAssignableFrom<IEnumerable<NotePushDto>>(Assert.IsType<OkObjectResult>(await Fns(db, target).ListPushes(Http.Empty(), ws.Id, Ct)).Value);
        Assert.Equal(n.Id, listed.Single().NoteId);
    }

    [Fact]
    public async Task Source_quote_is_left_out_when_not_requested()
    {
        var ws = _db.SeedWorkspace();
        var n = _db.SeedNote(ws.Id, evidenceSegmentId: ws.Segments.First().Id);
        var target = new FakeTarget();
        await using var db = _db.NewContext();
        await Fns(db, target).Push(Http.Json(Req(ws.Id, [n.Id], source: false)), SeedIntegration(), Ct);
        Assert.Null(target.Pushed.Single().SourceQuote);
    }

    [Fact]
    public async Task A_second_push_skips_already_pushed_notes_unless_repush()
    {
        var ws = _db.SeedWorkspace();
        var n = _db.SeedNote(ws.Id);
        var integration = SeedIntegration();
        var target = new FakeTarget();
        await using var db = _db.NewContext();
        var fns = Fns(db, target);

        await fns.Push(Http.Json(Req(ws.Id, [n.Id])), integration, Ct);
        var again = Outcomes(await fns.Push(Http.Json(Req(ws.Id, [n.Id])), integration, Ct)).Single();
        Assert.False(again.Ok);
        Assert.Equal("Already pushed.", again.Error);
        Assert.Single(target.Pushed);

        Assert.True(Outcomes(await fns.Push(Http.Json(Req(ws.Id, [n.Id], repush: true)), integration, Ct)).Single().Ok);
        Assert.Equal(2, target.Pushed.Count);
    }

    [Fact]
    public async Task Choices_reach_the_target_and_a_different_project_is_not_a_duplicate()
    {
        var ws = _db.SeedWorkspace();
        var n = _db.SeedNote(ws.Id);
        var integration = SeedIntegration();
        var target = new FakeTarget();
        await using var db = _db.NewContext();
        var fns = Fns(db, target);

        await fns.Push(Http.Json(Req(ws.Id, [n.Id], area: " Proj\\Team ")), integration, Ct);
        Assert.Equal(new PushChoices("Task", "Proj", "Proj\\Team"), target.Choices.Single());

        Assert.False(Outcomes(await fns.Push(Http.Json(Req(ws.Id, [n.Id])), integration, Ct)).Single().Ok);          // same project: skipped
        Assert.True(Outcomes(await fns.Push(Http.Json(Req(ws.Id, [n.Id], scope: "Other")), integration, Ct)).Single().Ok);
        Assert.Equal(["Proj", "Other"], (await db.NotePushes.ToListAsync()).OrderBy(p => p.PushedAt).Select(p => p.Scope));
    }

    [Fact]
    public async Task A_project_is_required_by_platforms_that_have_one()
    {
        var ws = _db.SeedWorkspace();
        var n = _db.SeedNote(ws.Id);
        await using var db = _db.NewContext();
        var result = await Fns(db, new FakeTarget()).Push(Http.Json(Req(ws.Id, [n.Id], scope: " ")), SeedIntegration(), Ct);
        Assert.Contains("project", Assert.IsType<BadRequestObjectResult>(result).Value!.ToString());
    }

    [Fact]
    public async Task Merged_notes_and_other_workspaces_notes_are_not_pushed()
    {
        var ws = _db.SeedWorkspace();
        var keep = _db.SeedNote(ws.Id, "keep");
        var merged = _db.SeedNote(ws.Id, "merged", mergedIntoId: keep.Id);
        var otherWs = _db.SeedWorkspace();
        var foreign = _db.SeedNote(otherWs.Id, "foreign");
        var target = new FakeTarget();

        await using var db = _db.NewContext();
        var outcomes = Outcomes(await Fns(db, target).Push(Http.Json(Req(ws.Id, [keep.Id, merged.Id, foreign.Id])), SeedIntegration(), Ct));

        Assert.Equal([true, false, false], outcomes.Select(o => o.Ok));
        Assert.Equal(["keep"], target.Pushed.Select(p => p.Title));
    }

    [Fact]
    public async Task One_failed_note_does_not_stop_the_rest_but_a_bad_token_does()
    {
        var ws = _db.SeedWorkspace();
        var a = _db.SeedNote(ws.Id, "a"); var b = _db.SeedNote(ws.Id, "b"); var c = _db.SeedNote(ws.Id, "c");
        var integration = SeedIntegration();
        var target = new FakeTarget { FailTitles = { "b" } };

        await using var db = _db.NewContext();
        var mixed = Outcomes(await Fns(db, target).Push(Http.Json(Req(ws.Id, [a.Id, b.Id, c.Id])), integration, Ct));
        Assert.Equal([true, false, true], mixed.Select(o => o.Ok));
        Assert.Equal(2, await db.NotePushes.CountAsync());

        var ws2 = _db.SeedWorkspace();
        var d = _db.SeedNote(ws2.Id, "d"); var e = _db.SeedNote(ws2.Id, "e");
        var bad = Outcomes(await Fns(db, new FakeTarget { AuthFails = true }).Push(Http.Json(Req(ws2.Id, [d.Id, e.Id])), integration, Ct));
        Assert.All(bad, o => Assert.False(o.Ok));
        Assert.Contains("Not attempted", bad[1].Error);
    }

    [Fact]
    public async Task Another_users_connection_workspace_and_pushes_are_404()
    {
        var ws = _db.SeedWorkspace();
        var n = _db.SeedNote(ws.Id);
        var integration = SeedIntegration();
        var target = new FakeTarget();

        await using var asB = _db.NewContext(TestDb.UserB);
        var fns = Fns(asB, target);
        Assert.IsType<NotFoundResult>(await fns.Push(Http.Json(Req(ws.Id, [n.Id])), integration, Ct));
        Assert.IsType<NotFoundResult>(await fns.Options(Http.Empty(), integration, Ct));
        Assert.IsType<NotFoundResult>(await fns.ListPushes(Http.Empty(), ws.Id, Ct));

        // B's own connection, A's workspace: still 404, nothing pushed.
        var bIntegration = SeedIntegration(TestDb.UserB);
        Assert.IsType<NotFoundResult>(await Fns(asB, target).Push(Http.Json(Req(ws.Id, [n.Id])), bIntegration, Ct));
        Assert.Empty(target.Pushed);
    }

    [Fact]
    public async Task Rejects_empty_oversized_and_typeless_requests_and_unsupported_platforms()
    {
        var ws = _db.SeedWorkspace();
        var integration = SeedIntegration();
        await using var db = _db.NewContext();
        var fns = Fns(db, new FakeTarget());

        Assert.IsType<BadRequestObjectResult>(await fns.Push(Http.Json(Req(ws.Id, [])), integration, Ct));
        Assert.IsType<BadRequestObjectResult>(await fns.Push(Http.Json(Req(ws.Id, Enumerable.Range(0, PushSupport.MaxNotes + 1).Select(_ => Guid.NewGuid()))), integration, Ct));
        Assert.IsType<BadRequestObjectResult>(await fns.Push(Http.Json(new PushRequest(ws.Id, [Guid.NewGuid()], " ", Scope: "Proj")), integration, Ct));

        var github = SeedIntegration(platform: IntegrationPlatform.GitHub);
        Assert.IsType<BadRequestObjectResult>(await fns.Push(Http.Json(Req(ws.Id, [Guid.NewGuid()])), github, Ct));
    }
}
