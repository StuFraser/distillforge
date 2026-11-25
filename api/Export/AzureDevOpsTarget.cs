using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using DistillForge.Core.Export;
using DistillForge.Core.Integrations;

namespace DistillForge.Api.Export;

/// <summary>Creates work items through the Azure DevOps Services REST API (7.1) with a personal access token.</summary>
public sealed class AzureDevOpsTarget(IHttpClientFactory httpFactory) : IExportTarget
{
    public const string ApiVersion = "7.1";
    public const string HttpClientName = "export";
    public const int MaxTitle = 255, MaxQuote = 600;

    public IntegrationPlatform Platform => IntegrationPlatform.AzureDevOps;
    public string? ScopeLabel => "Project";

    public async Task<PushOptionsDto> GetOptionsAsync(TargetConnection c, string? scope, CancellationToken ct)
    {
        try
        {
            IReadOnlyList<string>? projects = null;
            var chosen = scope?.Trim();
            if (string.IsNullOrEmpty(chosen))
            {
                var fallback = c.Setting("project");
                try { projects = await ListProjectsAsync(c, ct); }
                catch (ExportAuthException) when (fallback.Length > 0)
                {
                    projects = [fallback];   // the token can't list projects (no Project and Team scope), but a default is saved
                }
                if (projects.Count == 0) return new([], null, "No projects were found in the organisation.", ScopeLabel);
                chosen = projects.Contains(fallback) ? fallback : projects[0];
            }

            using var request = Build(c, HttpMethod.Get, chosen, $"_apis/wit/workitemtypes?api-version={ApiVersion}");
            var json = await SendAsync(request, ct);
            var types = (json?["value"] as JsonArray ?? [])
                .Where(t => t?["isDisabled"]?.GetValue<bool>() != true)
                .Select(t => t?["name"]?.GetValue<string>())
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n!)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (types.Count == 0) return new([], null, $"“{chosen}” has no work item types available.", ScopeLabel, projects, chosen);
            return new(types, types.Contains("Task") ? "Task" : types[0], null, ScopeLabel, projects, chosen);
        }
        catch (ExportAuthException ex) { return new([], null, ex.Message, ScopeLabel); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            return new([], null, $"Couldn't reach Azure DevOps: {ex.Message}", ScopeLabel);
        }
    }

    private async Task<IReadOnlyList<string>> ListProjectsAsync(TargetConnection c, CancellationToken ct)
    {
        using var request = Build(c, HttpMethod.Get, null, $"_apis/projects?api-version={ApiVersion}&$top=500");
        var json = await SendAsync(request, ct);
        return (json?["value"] as JsonArray ?? [])
            .Select(p => p?["name"]?.GetValue<string>())
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n!)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<PushOutcome> PushAsync(TargetConnection c, ExportItem item, PushChoices choices, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(choices.Scope)) return new(item.NoteId, false, null, null, "Choose a project.");
        try
        {
            using var request = Build(c, HttpMethod.Post, choices.Scope.Trim(), $"_apis/wit/workitems/${Uri.EscapeDataString(choices.ItemType)}?api-version={ApiVersion}");
            request.Content = new StringContent(BuildPatch(item, choices.AreaPath).ToJsonString(), Encoding.UTF8, "application/json-patch+json");
            var json = await SendAsync(request, ct);
            var id = json?["id"]?.ToString();
            var url = json?["_links"]?["html"]?["href"]?.GetValue<string>();
            return id is null
                ? new(item.NoteId, false, null, null, "Azure DevOps answered without a work item id.")
                : new(item.NoteId, true, id, url, null);
        }
        catch (ExportAuthException) { throw; }
        catch (AdoRequestException ex) { return new(item.NoteId, false, null, null, ex.Message); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            return new(item.NoteId, false, null, null, $"Couldn't reach Azure DevOps: {ex.Message}");
        }
    }

    /// <summary>JSON Patch document for a new work item. Public so the field mapping can be tested without HTTP.</summary>
    public static JsonArray BuildPatch(ExportItem item, string? areaPath)
    {
        var ops = new JsonArray { Add("System.Title", Clip(Collapse(item.Title), MaxTitle)), Add("System.Description", BuildDescriptionHtml(item)) };
        if (!string.IsNullOrWhiteSpace(areaPath)) ops.Add(Add("System.AreaPath", areaPath.Trim()));
        var tag = item.CategoryName?.Replace(";", " ").Trim();
        if (!string.IsNullOrEmpty(tag)) ops.Add(Add("System.Tags", Clip(tag, 100)));
        return ops;

        static JsonObject Add(string field, string value) => new() { ["op"] = "add", ["path"] = "/fields/" + field, ["value"] = value };
    }

    /// <summary>The description is HTML in Azure DevOps, so every user-supplied value is encoded.</summary>
    public static string BuildDescriptionHtml(ExportItem item)
    {
        static string E(string s) => WebUtility.HtmlEncode(s);
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(item.Summary)) sb.Append("<p>").Append(E(Collapse(item.Summary))).Append("</p>");

        var meta = new List<string>();
        if (!string.IsNullOrWhiteSpace(item.Owner)) meta.Add("<b>Owner:</b> " + E(Collapse(item.Owner)));
        if (!string.IsNullOrWhiteSpace(item.Due)) meta.Add("<b>Due:</b> " + E(Collapse(item.Due)));
        if (meta.Count > 0) sb.Append("<p>").Append(string.Join(" &nbsp;·&nbsp; ", meta)).Append("</p>");

        if (!string.IsNullOrWhiteSpace(item.SourceQuote))
        {
            sb.Append("<blockquote>“").Append(E(Clip(Collapse(item.SourceQuote), MaxQuote))).Append("”</blockquote>");
            if (!string.IsNullOrWhiteSpace(item.SourceLabel)) sb.Append("<p><i>Source: ").Append(E(item.SourceLabel)).Append("</i></p>");
        }
        sb.Append("<hr><p><i>Created by DistillForge from “").Append(E(item.WorkspaceTitle)).Append("”.</i></p>");
        return sb.ToString();
    }

    /// <param name="project">Null for organisation-level calls (listing projects).</param>
    private HttpRequestMessage Build(TargetConnection c, HttpMethod method, string? project, string relative)
    {
        var org = c.Setting("organizationUrl").TrimEnd('/');
        var field = IntegrationCatalog.Field(IntegrationPlatform.AzureDevOps, "organizationUrl");
        // Re-checked here, not just when saved: the server must never call a host that isn't Azure DevOps.
        if (!IntegrationValidation.HostAllowed(org, field!.AllowedHosts!))
            throw new ExportAuthException("The organisation URL isn't an Azure DevOps address. Edit the connection.");
        // The project name comes from the client, so it is escaped into a single path segment and can't redirect the call elsewhere.
        var prefix = project is null ? org : $"{org}/{Uri.EscapeDataString(project)}";
        var request = new HttpRequestMessage(method, $"{prefix}/{relative}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(":" + c.Secret("pat"))));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private async Task<JsonNode?> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var http = httpFactory.CreateClient(HttpClientName);
        http.Timeout = TimeSpan.FromSeconds(30);
        using var response = await http.SendAsync(request, ct);
        var raw = await response.Content.ReadAsStringAsync(ct);

        // A bad token often comes back as 203 with an HTML sign-in page rather than 401.
        if (response.StatusCode is HttpStatusCode.NonAuthoritativeInformation or HttpStatusCode.Unauthorized or HttpStatusCode.Found or HttpStatusCode.Redirect)
            throw new ExportAuthException("Azure DevOps rejected the token. Check it hasn't expired and belongs to this organisation.");
        if (response.StatusCode == HttpStatusCode.Forbidden)
            throw new ExportAuthException("The token isn't allowed to do that. It needs the Project and Team (Read) and Work Items (Read & write) scopes.");
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new ExportAuthException("Azure DevOps couldn't find that organisation or project. Check the connection's URL and project name.");
        if (!response.IsSuccessStatusCode)
        {
            string? message = null;
            try { message = JsonNode.Parse(raw)?["message"]?.GetValue<string>(); } catch (System.Text.Json.JsonException) { }
            throw new AdoRequestException($"Azure DevOps refused it ({(int)response.StatusCode}): {Clip(Collapse(message ?? response.ReasonPhrase ?? "no details"), 300)}");
        }
        return JsonNode.Parse(raw);
    }

    private sealed class AdoRequestException(string message) : Exception(message);

    private static string Collapse(string s) => string.Join(' ', s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    private static string Clip(string s, int max) => s.Length <= max ? s : s[..(max - 1)].TrimEnd() + "…";
}
