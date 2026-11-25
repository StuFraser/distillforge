using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;

namespace DistillForge.Api.Export;

public sealed record TrelloTokens(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt);

/// <summary>The sign-in was refused or failed; the message is safe to show the user.</summary>
public sealed class TrelloOAuthException(string message) : Exception(message);

/// <summary>Exchanges an authorization code (and later a refresh token) for Trello tokens. Talks only to the configured token URL.</summary>
public sealed class TrelloOAuthClient(IHttpClientFactory httpFactory, IOptions<TrelloOptions> options, TimeProvider clock)
{
    public bool Configured => options.Value.Configured;

    public Task<TrelloTokens> ExchangeCodeAsync(string code, string codeVerifier, string redirectUri, CancellationToken ct) =>
        PostAsync(new JsonObject
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["code_verifier"] = codeVerifier,
            ["redirect_uri"] = redirectUri,
        }, previousRefreshToken: null, ct);

    /// <summary>Refresh tokens are single-use, so the response normally carries a new one that must replace the old.</summary>
    public Task<TrelloTokens> RefreshAsync(string refreshToken, CancellationToken ct) =>
        PostAsync(new JsonObject { ["grant_type"] = "refresh_token", ["refresh_token"] = refreshToken }, refreshToken, ct);

    private async Task<TrelloTokens> PostAsync(JsonObject body, string? previousRefreshToken, CancellationToken ct)
    {
        var o = options.Value;
        if (!o.Configured) throw new TrelloOAuthException("Trello isn't set up on this server (Trello__ClientId is missing).");
        body["client_id"] = o.ClientId!.Trim();
        if (!string.IsNullOrWhiteSpace(o.ClientSecret)) body["client_secret"] = o.ClientSecret.Trim();   // confidential clients only

        try
        {
            var http = httpFactory.CreateClient(AzureDevOpsTarget.HttpClientName);
            http.Timeout = TimeSpan.FromSeconds(30);
            using var request = new HttpRequestMessage(HttpMethod.Post, o.ResolvedTokenUrl)
            {
                Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
            };
            using var response = await http.SendAsync(request, ct);
            var raw = await response.Content.ReadAsStringAsync(ct);

            JsonNode? json = null;
            try { json = JsonNode.Parse(raw); } catch (System.Text.Json.JsonException) { }

            if (!response.IsSuccessStatusCode)
            {
                var detail = json?["error_description"]?.GetValue<string>() ?? json?["error"]?.GetValue<string>();
                if (json?["error"]?.GetValue<string>() == "invalid_grant")
                    throw new TrelloOAuthException("Trello says that approval has expired or was already used. Connect again.");
                throw new TrelloOAuthException($"Trello refused the sign-in ({(int)response.StatusCode}): {Clip(detail ?? (string.IsNullOrWhiteSpace(raw) ? response.ReasonPhrase ?? "no details" : raw), 300)}");
            }

            var access = json?["access_token"]?.GetValue<string>();
            if (string.IsNullOrEmpty(access)) throw new TrelloOAuthException("Trello answered without an access token.");
            var refresh = json?["refresh_token"]?.GetValue<string>() ?? previousRefreshToken;
            if (string.IsNullOrEmpty(refresh))
                throw new TrelloOAuthException("Trello didn't return a refresh token, so the connection would stop working within the hour. Check that offline_access is enabled for the OAuth client.");
            var seconds = json?["expires_in"] is { } e ? Math.Clamp(e.GetValue<int>(), 60, 86_400) : 3600;
            return new TrelloTokens(access, refresh, clock.GetUtcNow().AddSeconds(seconds));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new TrelloOAuthException($"Couldn't reach Trello's sign-in service: {ex.Message}");
        }
    }

    private static string Clip(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";
}
