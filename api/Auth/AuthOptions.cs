namespace DistillForge.Api.Auth;

public enum AuthMode
{
    /// <summary>Validate Entra ID access tokens (the default; also what any deployed API must use).</summary>
    Entra,
    /// <summary>A fixed fake user, no tokens. Development only: the API refuses to start with this outside Development.</summary>
    Dev,
}

/// <summary>Bound from the <c>Auth__*</c> settings.</summary>
public sealed class AuthOptions
{
    public const string Section = "Auth";

    public AuthMode Mode { get; set; } = AuthMode.Entra;

    /// <summary>Directory (tenant) id of the single tenant whose users may sign in.</summary>
    public string? TenantId { get; set; }

    /// <summary>Application (client) id of the "DistillForge API" app registration.</summary>
    public string? ClientId { get; set; }

    /// <summary>Optional extra accepted audience, normally the Application ID URI (<c>api://&lt;client-id&gt;</c>). v2 tokens carry the client id; the URI is accepted too.</summary>
    public string? Audience { get; set; }

    /// <summary>Delegated scope the token must carry (the <c>scp</c> claim).</summary>
    public string RequiredScope { get; set; } = "access_as_user";

    /// <summary>
    /// One-off migration aid: when a user signs in for the first time, rows that pre-date auth (no owner) are handed to them.
    /// Switch on for the first real sign-in, then off again.
    /// </summary>
    public bool ClaimOrphanedData { get; set; }

    /// <summary>The problems with this configuration, or an empty list when it is usable in the given environment.</summary>
    public IReadOnlyList<string> Validate(bool isDevelopment)
    {
        var problems = new List<string>();
        if (Mode == AuthMode.Dev)
        {
            if (!isDevelopment)
                problems.Add("Auth__Mode=Dev is only allowed in the Development environment (it disables sign-in).");
            return problems;
        }
        if (string.IsNullOrWhiteSpace(TenantId)) problems.Add("Auth__TenantId is not set.");
        else if (!Guid.TryParse(TenantId, out _)) problems.Add("Auth__TenantId must be the tenant's GUID.");
        if (string.IsNullOrWhiteSpace(ClientId)) problems.Add("Auth__ClientId is not set.");
        if (problems.Count > 0)
            problems.Add("Configure the Entra ID app registration (see design-notes/entraId_provisioning.md) or, for local work, set Auth__Mode=Dev.");
        return problems;
    }
}
