namespace DistillForge.Core.Auth;

/// <summary>
/// The front end's sign-in settings (the "Auth" section of wwwroot/appsettings.json). All of it is public by nature.
/// Leave it empty and the app runs in dev mode: no sign-in, no tokens (the API must then be in Auth__Mode=Dev).
/// </summary>
public sealed class ClientAuthSettings
{
    public const string Section = "Auth";

    /// <summary>Directory (tenant) id. Used to build the authority unless <see cref="Authority"/> is given.</summary>
    public string? TenantId { get; set; }

    /// <summary>Full authority URL override, e.g. for Entra External ID later. Normally left empty.</summary>
    public string? Authority { get; set; }

    /// <summary>Application (client) id of the "DistillForge Web" SPA registration.</summary>
    public string? ClientId { get; set; }

    /// <summary>Scope to request for the API, e.g. <c>api://&lt;api-client-id&gt;/access_as_user</c>.</summary>
    public string? ApiScope { get; set; }

    /// <summary>True when any sign-in setting is present. Entra sign-in is on; <see cref="Validate"/> says whether it is complete.</summary>
    public bool IsEnabled =>
        !string.IsNullOrWhiteSpace(ClientId) || !string.IsNullOrWhiteSpace(TenantId) ||
        !string.IsNullOrWhiteSpace(Authority) || !string.IsNullOrWhiteSpace(ApiScope);

    public string ResolvedAuthority =>
        !string.IsNullOrWhiteSpace(Authority) ? Authority.Trim().TrimEnd('/') : $"https://login.microsoftonline.com/{TenantId!.Trim()}";

    /// <summary>What is missing or malformed. Empty when dev mode (nothing set) or fully configured.</summary>
    public IReadOnlyList<string> Validate()
    {
        var problems = new List<string>();
        if (!IsEnabled) return problems;
        if (string.IsNullOrWhiteSpace(ClientId)) problems.Add("Auth:ClientId (the SPA app registration's client id) is not set.");
        if (string.IsNullOrWhiteSpace(TenantId) && string.IsNullOrWhiteSpace(Authority)) problems.Add("Auth:TenantId (or Auth:Authority) is not set.");
        else if (string.IsNullOrWhiteSpace(Authority) && !Guid.TryParse(TenantId, out _)) problems.Add("Auth:TenantId must be the tenant's GUID.");
        if (string.IsNullOrWhiteSpace(ApiScope)) problems.Add("Auth:ApiScope (api://<api-client-id>/access_as_user) is not set.");
        return problems;
    }
}
