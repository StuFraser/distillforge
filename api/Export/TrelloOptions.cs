namespace DistillForge.Api.Export;

/// <summary>
/// DistillForge's Trello OAuth 2.0 app (one Power-Up for the whole service). Users approve access to their own Trello account;
/// the resulting tokens are stored per connection. Config: Trello__ClientId (required for Connect), Trello__ClientSecret (only
/// if the client is "confidential"; a public client uses PKCE alone).
/// </summary>
public sealed class TrelloOptions
{
    public const string Section = "Trello";
    public const string DefaultTokenUrl = "https://auth.atlassian.com/oauth/token";
    public const string DefaultApiBaseUrl = "https://api.trello.com/1/";

    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    /// <summary>Trello's docs name two different token URLs; this is overridable (Trello__TokenUrl) in case the default is wrong.</summary>
    public string? TokenUrl { get; set; }
    /// <summary>Where REST calls go (Trello__ApiBaseUrl), with a trailing slash.</summary>
    public string? ApiBaseUrl { get; set; }

    public bool Configured => !string.IsNullOrWhiteSpace(ClientId);
    public string ResolvedTokenUrl => string.IsNullOrWhiteSpace(TokenUrl) ? DefaultTokenUrl : TokenUrl.Trim();
    public string ResolvedApiBaseUrl => string.IsNullOrWhiteSpace(ApiBaseUrl) ? DefaultApiBaseUrl : ApiBaseUrl.Trim().TrimEnd('/') + "/";
}
