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

    public async Task<PushOptionsDto> GetOptionsAsync(TargetConnection c, CancellationToken ct)
    {
        try
        {
            using var request = Build(c, HttpMethod.Get, $"_apis/wit/workitemtypes?api-version={ApiVersion}");
            var json = await SendAsync(request, ct);
            var types = (json?["value"] as JsonArray ?? [])
                .Where(t => t?["isDisabled"]?.GetValue<bool>() != true)
                .Select(t => t?["name"]?.GetValue<string>())
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n!)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (types.Count == 0) return new([], null, "The project has no work item types available.");
            return new(types, types.Contains("Task") ? "Task" : types[0], null);
        }
        catch (ExportAuthException ex) { return new([], null, ex.Message); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            return new([], null, $"Couldn't reach Azure DevOps: {ex.Message}");
        }
    }

    public async Task<PushOutcome> PushAsync(TargetConnection c, ExportItem item, string itemType, CancellationToken ct)
    {
        try
        {
            using var request = Build(c, HttpMethod.Post, $"_apis/wit/workitems/${Uri.EscapeDataString(itemType)}?api-version={ApiVersion}");
            request.Content = new StringContent(BuildPatch(item, itemType, c.Setting("areaPath")).ToJsonString(), Encoding.UTF8, "application/json-patch+json");
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
    public static JsonArray BuildPatch(ExportItem item, string itemType, string? areaPath)
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

    private HttpRequestMessage Build(TargetConnection c, HttpMethod method, string relative)
    {
        var org = c.Setting("organizationUrl").TrimEnd('/');
        var field = IntegrationCatalog.Field(IntegrationPlatform.AzureDevOps, "organizationUrl");
        // Re-checked here, not just when saved: the server must never call a host that isn't Azure DevOps.
        if (!IntegrationValidation.HostAllowed(org, field!.AllowedHosts!))
            throw new ExportAuthException("The organisation URL isn't an Azure DevOps address. Edit the connection.");
        var project = Uri.EscapeDataString(c.Setting("project"));
        var request = new HttpRequestMessage(method, $"{org}/{project}/{relative}");
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
            throw new ExportAuthException("The token isn't allowed to do that. It needs the Work Items (Read & write) scope.");
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
