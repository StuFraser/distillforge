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

    [Fact] public void Optional_secret_may_be_absent()
    {
        var trello = new IntegrationRequest(IntegrationPlatform.Trello, "t",
            new() { ["apiKey"] = "k", ["token"] = "t", ["listId"] = "l" });
        Assert.Null(IntegrationValidation.Validate(trello));
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
