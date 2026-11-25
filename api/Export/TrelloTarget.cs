using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using DistillForge.Core.Export;
using DistillForge.Core.Integrations;

namespace DistillForge.Api.Export;

/// <summary>
/// Creates Trello cards using the user's OAuth 2.0 access token (Bearer header, never in the URL), renewing it with the refresh token
/// when it is about to expire. The "scope" is the board and the "item type" is the list the card goes to; both are Trello ids chosen
/// from lists fetched live. The host comes from server config, never from the user.
/// </summary>
public sealed partial class TrelloTarget(
    IHttpClientFactory httpFactory, Microsoft.Extensions.Options.IOptions<TrelloOptions> options, TrelloOAuthClient oauth, TimeProvider clock) : IExportTarget
{
    public const int MaxName = 300;
    private static readonly TimeSpan RefreshWindow = TimeSpan.FromMinutes(2);

    public IntegrationPlatform Platform => IntegrationPlatform.Trello;
    public string? ScopeLabel => "Board";

    [GeneratedRegex("^[0-9a-f]{24}$")]
    private static partial Regex IdPattern();

    /// <summary>Ids come from the client and end up in a URL path, so only Trello's own 24-hex-character form is accepted.</summary>
    public static bool IsId(string? s) => s is not null && IdPattern().IsMatch(s);

    public async Task<PushOptionsDto> GetOptionsAsync(TargetConnection c, string? scope, CancellationToken ct)
    {
        try
        {
            IReadOnlyList<PushOption>? boards = null;
            var chosen = scope?.Trim();
            if (string.IsNullOrEmpty(chosen))
            {
                var json = await GetAsync(c, "members/me/boards?filter=open&fields=name", ct);
                boards = Options(json);
                if (boards.Count == 0) return Fail("No open boards were found for this token.");
                chosen = boards[0].Value;
            }
            else if (!IsId(chosen)) return Fail("That isn't a Trello board.");

            var lists = Options(await GetAsync(c, $"boards/{chosen}/lists?filter=open&fields=name", ct));
            if (lists.Count == 0) return new([], null, "That board has no open lists. Add one in Trello first.", ScopeLabel, boards, chosen, "Add to list");
            return new(lists, lists[0].Value, null, ScopeLabel, boards, chosen, "Add to list");
        }
        catch (ExportAuthException ex) { return Fail(ex.Message); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            return Fail($"Couldn't reach Trello: {ex.Message}");
        }

        PushOptionsDto Fail(string error) => new([], null, error, ScopeLabel, ItemTypeLabel: "Add to list");
    }

    private static List<PushOption> Options(JsonNode? json) =>
        (json as JsonArray ?? [])
            .Select(n => (Id: n?["id"]?.GetValue<string>(), Name: n?["name"]?.GetValue<string>()))
            .Where(x => IsId(x.Id) && !string.IsNullOrWhiteSpace(x.Name))
            .Select(x => new PushOption(x.Id!, x.Name!))
            .ToList();

    public async Task<PushOutcome> PushAsync(TargetConnection c, ExportItem item, PushChoices choices, CancellationToken ct)
    {
        if (!IsId(choices.ItemType)) return new(item.NoteId, false, null, null, "Choose a list.");
        try
        {
            using var request = Build(c, HttpMethod.Post, "cards");
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["idList"] = choices.ItemType,
                ["name"] = Clip(Collapse(item.Title), MaxName),
                ["desc"] = BuildDescription(item),
                ["pos"] = "bottom",
            });
            var json = await SendAsync(request, ct);
            var id = json?["id"]?.GetValue<string>();
            return id is null
                ? new(item.NoteId, false, null, null, "Trello answered without a card id.")
                : new(item.NoteId, true, json?["idShort"]?.ToString() ?? id, json?["shortUrl"]?.GetValue<string>() ?? json?["url"]?.GetValue<string>(), null);
        }
        catch (ExportAuthException) { throw; }
        catch (TrelloRequestException ex) { return new(item.NoteId, false, null, null, ex.Message); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            return new(item.NoteId, false, null, null, $"Couldn't reach Trello: {ex.Message}");
        }
    }

    /// <summary>Card descriptions are Markdown.</summary>
    public static string BuildDescription(ExportItem item)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(item.Summary)) parts.Add(Collapse(item.Summary));

        var meta = new List<string>();
        if (!string.IsNullOrWhiteSpace(item.Owner)) meta.Add("**Owner:** " + Collapse(item.Owner));
        if (!string.IsNullOrWhiteSpace(item.Due)) meta.Add("**Due:** " + Collapse(item.Due));
        if (!string.IsNullOrWhiteSpace(item.CategoryName)) meta.Add("**Category:** " + Collapse(item.CategoryName));
        if (meta.Count > 0) parts.Add(string.Join("  ·  ", meta));

        if (!string.IsNullOrWhiteSpace(item.SourceQuote))
        {
            var quote = "> “" + Clip(Collapse(item.SourceQuote), AzureDevOpsTarget.MaxQuote) + "”";
            parts.Add(string.IsNullOrWhiteSpace(item.SourceLabel) ? quote : quote + "\n> \n> _Source: " + Collapse(item.SourceLabel) + "_");
        }
        parts.Add("---\n_Created by DistillForge from “" + Collapse(item.WorkspaceTitle) + "”._");
        return string.Join("\n\n", parts);
    }

    public static Dictionary<string, string> ToSecrets(TrelloTokens t) => new()
    {
        ["accessToken"] = t.AccessToken,
        ["refreshToken"] = t.RefreshToken,
        ["expiresAt"] = t.ExpiresAt.ToString("O"),
    };

    public async Task<IReadOnlyDictionary<string, string>?> RefreshSecretsAsync(TargetConnection c, CancellationToken ct)
    {
        if (c.Secret("accessToken").Length == 0)
            throw new ExportAuthException("This Trello connection uses an older sign-in that no longer works. Edit it and choose Reconnect.");

        var expires = DateTimeOffset.TryParse(c.Secret("expiresAt"), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var e)
            ? e : DateTimeOffset.MinValue;
        if (expires - clock.GetUtcNow() > RefreshWindow) return null;

        var refresh = c.Secret("refreshToken");
        if (refresh.Length == 0) throw new ExportAuthException("Trello access has expired. Edit the connection and choose Reconnect.");
        try { return ToSecrets(await oauth.RefreshAsync(refresh, ct)); }
        catch (TrelloOAuthException ex)
        {
            throw new ExportAuthException($"Trello access has expired and couldn't be renewed ({ex.Message}). Edit the connection and choose Reconnect.");
        }
    }

    private HttpRequestMessage Build(TargetConnection c, HttpMethod method, string relative)
    {
        var request = new HttpRequestMessage(method, options.Value.ResolvedApiBaseUrl + relative);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", c.Secret("accessToken"));
        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private async Task<JsonNode?> GetAsync(TargetConnection c, string relative, CancellationToken ct)
    {
        using var request = Build(c, HttpMethod.Get, relative);
        return await SendAsync(request, ct);
    }

    private async Task<JsonNode?> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var http = httpFactory.CreateClient(AzureDevOpsTarget.HttpClientName);
        http.Timeout = TimeSpan.FromSeconds(30);
        using var response = await http.SendAsync(request, ct);
        var raw = await response.Content.ReadAsStringAsync(ct);

        // The detail is shown on purpose: whether Trello's REST host accepts this token, and which scopes it needs, is only learned by trying.
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new ExportAuthException($"Trello didn't accept the sign-in ({Clip(Collapse(string.IsNullOrWhiteSpace(raw) ? "no details" : raw), 200)}). Edit the connection and choose Reconnect.");
        if (response.StatusCode == HttpStatusCode.Forbidden)
            throw new ExportAuthException($"Trello says this connection isn't allowed to do that ({Clip(Collapse(string.IsNullOrWhiteSpace(raw) ? "no details" : raw), 200)}). It may be missing a scope on the OAuth client.");
        if (!response.IsSuccessStatusCode)
        {
            var detail = Clip(Collapse(string.IsNullOrWhiteSpace(raw) ? response.ReasonPhrase ?? "no details" : raw), 300);
            throw new TrelloRequestException($"Trello refused it ({(int)response.StatusCode}): {detail}");
        }
        return JsonNode.Parse(raw);
    }

    private sealed class TrelloRequestException(string message) : Exception(message);

    private static string Collapse(string s) => string.Join(' ', s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    private static string Clip(string s, int max) => s.Length <= max ? s : s[..(max - 1)].TrimEnd() + "…";
}
