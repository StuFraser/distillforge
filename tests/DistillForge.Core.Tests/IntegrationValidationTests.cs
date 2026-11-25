using DistillForge.Core.Integrations;

namespace DistillForge.Core.Tests;

public class IntegrationValidationTests
{
    private static IntegrationRequest GitHub(Action<Dictionary<string, string?>>? tweak = null)
    {
        var v = new Dictionary<string, string?> { ["owner"] = "me", ["repo"] = "r", ["token"] = "ghp_x" };
        tweak?.Invoke(v);
        return new(IntegrationPlatform.GitHub, "My repo", v);
    }

    [Fact] public void Valid_request_passes() => Assert.Null(IntegrationValidation.Validate(GitHub()));

    [Fact] public void Missing_required_field_is_reported_by_label() =>
        Assert.Equal("Repository is required.", IntegrationValidation.Validate(GitHub(v => v["repo"] = " ")));

    [Fact] public void Unknown_field_is_rejected() =>
        Assert.Contains("nope", IntegrationValidation.Validate(GitHub(v => v["nope"] = "x")));

    [Fact] public void Secret_is_required_on_create_but_may_be_omitted_when_already_stored()
    {
        var noToken = GitHub(v => v.Remove("token"));
        Assert.Equal("Personal access token is required.", IntegrationValidation.Validate(noToken));
        Assert.Null(IntegrationValidation.Validate(noToken, ["token"]));
    }

    [Fact] public void Clearing_a_required_secret_is_rejected() =>
        Assert.NotNull(IntegrationValidation.Validate(GitHub(v => v["token"] = ""), ["token"]));

    [Fact] public void Trello_is_connected_by_signing_in_so_it_has_no_fields()
    {
        var trello = IntegrationCatalog.Get(IntegrationPlatform.Trello);
        Assert.True(trello.UsesOAuth);
        Assert.Empty(trello.Fields);
        Assert.Null(IntegrationValidation.Validate(new(IntegrationPlatform.Trello, "t", new())));       // e.g. a rename
        Assert.NotNull(IntegrationValidation.Validate(new(IntegrationPlatform.Trello, "t", new() { ["token"] = "x" })));
        Assert.All(IntegrationCatalog.All.Where(p => p.Platform != IntegrationPlatform.Trello), p => Assert.False(p.UsesOAuth));
    }

    [Fact] public void Trello_authorize_url_uses_the_code_flow_with_pkce_and_offline_access()
    {
        var url = TrelloAuth.AuthorizeUrl(" CLIENT ", "http://localhost:5189/trello-callback.html", "STATE", "CHALLENGE");
        Assert.StartsWith("https://auth.atlassian.com/authorize?", url);
        Assert.Contains("client_id=CLIENT&", url);
        Assert.Contains("scope=read%3Aboard%3Atrello%20write%3Aboard%3Atrello%20offline_access", url);
        Assert.Contains("redirect_uri=http%3A%2F%2Flocalhost%3A5189%2Ftrello-callback.html", url);
        Assert.Contains("state=STATE", url);
        Assert.Contains("response_type=code", url);
        Assert.Contains("prompt=consent", url);
        Assert.Contains("code_challenge=CHALLENGE", url);
        Assert.Contains("code_challenge_method=S256", url);
    }

    [Fact] public void Pkce_matches_the_rfc_7636_example_and_generates_valid_random_values()
    {
        Assert.Equal("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM", Pkce.Challenge("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk"));
        var v = Pkce.NewVerifier();
        Assert.InRange(v.Length, 43, 128);
        Assert.Matches("^[A-Za-z0-9_-]+$", v);
        Assert.NotEqual(v, Pkce.NewVerifier());
        Assert.Matches("^[A-Za-z0-9_-]+$", Pkce.NewState());
    }

    [Fact] public void Url_fields_must_be_https()
    {
        IntegrationRequest Jira(string url) => new(IntegrationPlatform.Jira, "j", new()
        { ["siteUrl"] = url, ["email"] = "a@b.c", ["apiToken"] = "t", ["projectKey"] = "ABC" });
        Assert.Null(IntegrationValidation.Validate(Jira("https://x.atlassian.net/")));
        Assert.NotNull(IntegrationValidation.Validate(Jira("http://x.atlassian.net")));
        Assert.NotNull(IntegrationValidation.Validate(Jira("not a url")));
        Assert.NotNull(IntegrationValidation.Validate(Jira("https://x.atlassian.net?a=b")));
    }

    [Fact] public void Azure_DevOps_url_must_be_an_azure_devops_host()
    {
        IntegrationRequest Ado(string url) => new(IntegrationPlatform.AzureDevOps, "a", new()
        { ["organizationUrl"] = url, ["pat"] = "x" });
        Assert.Null(IntegrationValidation.Validate(Ado("https://dev.azure.com/org")));
        Assert.Null(IntegrationValidation.Validate(Ado("https://org.visualstudio.com")));
        Assert.NotNull(IntegrationValidation.Validate(Ado("https://visualstudio.com")));
        Assert.NotNull(IntegrationValidation.Validate(Ado("https://evil.example/dev.azure.com")));
        Assert.NotNull(IntegrationValidation.Validate(Ado("https://dev.azure.com.evil.example")));
        Assert.NotNull(IntegrationValidation.Validate(Ado("https://169.254.169.254")));
    }

    [Fact] public void CleanSettings_trims_applies_defaults_drops_blanks_and_excludes_secrets()
    {
        var r = new IntegrationRequest(IntegrationPlatform.Jira, "j", new()
        { ["siteUrl"] = " https://x.atlassian.net/ ", ["email"] = "a@b.c", ["apiToken"] = "secret", ["projectKey"] = "ABC", ["issueType"] = "" });
        var s = IntegrationValidation.CleanSettings(r);
        Assert.Equal("https://x.atlassian.net", s["siteUrl"]);
        Assert.Equal("Task", s["issueType"]);
        Assert.DoesNotContain("apiToken", s.Keys);
    }

    [Fact] public void Every_platform_has_unique_field_keys()
    {
        foreach (var p in IntegrationCatalog.All)
        {
            Assert.Equal(p.Fields.Count, p.Fields.Select(f => f.Key).Distinct().Count());
        }
        Assert.Equal(Enum.GetValues<IntegrationPlatform>().Length, IntegrationCatalog.All.Count);
    }
}
