namespace DistillForge.Core.Integrations;

/// <summary>Trello's OAuth 2.0 sign-in (authorization code + PKCE, through Atlassian's auth server).</summary>
public static class TrelloAuth
{
    public const string AuthorizeEndpoint = "https://auth.atlassian.com/authorize";

    /// <summary>offline_access is what makes Trello return a refresh token. Must be a subset of the scopes enabled on the OAuth client.</summary>
    public const string Scopes = "read:board:trello write:board:trello offline_access";

    /// <summary>The callback page must be registered as a callback URL on the OAuth client, exactly.</summary>
    public const string CallbackPage = "trello-callback.html";

    public static string AuthorizeUrl(string clientId, string redirectUri, string state, string codeChallenge) =>
        AuthorizeEndpoint
        + "?client_id=" + Uri.EscapeDataString(clientId.Trim())
        + "&scope=" + Uri.EscapeDataString(Scopes)
        + "&redirect_uri=" + Uri.EscapeDataString(redirectUri)
        + "&state=" + Uri.EscapeDataString(state)
        + "&response_type=code"
        + "&prompt=consent"
        + "&code_challenge=" + Uri.EscapeDataString(codeChallenge)
        + "&code_challenge_method=S256";
}
