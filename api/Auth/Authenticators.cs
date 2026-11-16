using System.Security.Claims;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace DistillForge.Api.Auth;

/// <summary>Who is calling, as proven by the token: tenant + object id are the identity; the name is for display only.</summary>
public sealed record AuthenticatedUser(string TenantId, string ObjectId, string? DisplayName);

public sealed record AuthResult(AuthenticatedUser? User, string? Failure)
{
    public bool Succeeded => User is not null;
    public static AuthResult Ok(AuthenticatedUser user) => new(user, null);
    public static AuthResult Fail(string reason) => new(null, reason);
}

public interface IRequestAuthenticator
{
    /// <param name="authorizationHeader">The raw <c>Authorization</c> header value, or null when absent.</param>
    Task<AuthResult> AuthenticateAsync(string? authorizationHeader, CancellationToken ct);
}

/// <summary>The fake user for <c>Auth__Mode=Dev</c>. A fixed identity, so data created in dev stays yours between runs.</summary>
public sealed class DevAuthenticator : IRequestAuthenticator
{
    public static readonly AuthenticatedUser DevUser =
        new("00000000-0000-0000-0000-000000000000", "00000000-0000-0000-0000-0000000000d3", "Dev User");

    public Task<AuthResult> AuthenticateAsync(string? authorizationHeader, CancellationToken ct) =>
        Task.FromResult(AuthResult.Ok(DevUser));
}

/// <summary>
/// Validates an Entra ID v2.0 access token for the single configured tenant: signature (keys from the tenant's published
/// metadata), issuer, audience, lifetime, the tenant claim and the required delegated scope. Anything else is rejected.
/// </summary>
public sealed class JwtAuthenticator : IRequestAuthenticator
{
    private readonly AuthOptions _options;
    private readonly IConfigurationManager<OpenIdConnectConfiguration> _metadata;
    private readonly ILogger<JwtAuthenticator> _logger;
    private readonly JsonWebTokenHandler _handler = new();

    public JwtAuthenticator(IOptions<AuthOptions> options, IConfigurationManager<OpenIdConnectConfiguration> metadata, ILogger<JwtAuthenticator> logger)
    {
        _options = options.Value;
        _metadata = metadata;
        _logger = logger;
    }

    public static string Authority(string tenantId) => $"https://login.microsoftonline.com/{tenantId}/v2.0";

    public async Task<AuthResult> AuthenticateAsync(string? authorizationHeader, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(authorizationHeader)) return AuthResult.Fail("No Authorization header.");
        const string prefix = "Bearer ";
        if (!authorizationHeader.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return AuthResult.Fail("Authorization must be a Bearer token.");
        var token = authorizationHeader[prefix.Length..].Trim();
        if (token.Length == 0) return AuthResult.Fail("Empty bearer token.");

        OpenIdConnectConfiguration config;
        try { config = await _metadata.GetConfigurationAsync(ct); }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or IOException)
        {
            // Not the caller's fault, but we cannot vouch for anyone, so the answer is still no.
            _logger.LogError(ex, "Could not load the Entra ID signing keys.");
            return AuthResult.Fail("Token validation is temporarily unavailable.");
        }

        var audiences = new List<string> { _options.ClientId! };
        if (!string.IsNullOrWhiteSpace(_options.Audience)) audiences.Add(_options.Audience);
        audiences.Add($"api://{_options.ClientId}");

        var parameters = new TokenValidationParameters
        {
            ValidIssuer = Authority(_options.TenantId!),
            ValidAudiences = audiences,
            IssuerSigningKeys = config.SigningKeys,
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            RequireSignedTokens = true,
            RequireExpirationTime = true,
            ClockSkew = TimeSpan.FromMinutes(2),
        };

        var result = await _handler.ValidateTokenAsync(token, parameters);
        if (!result.IsValid)
        {
            // Key rotation: Entra publishes new keys before using them, but refresh once if the signing key wasn't found.
            if (result.Exception is SecurityTokenSignatureKeyNotFoundException)
            {
                _metadata.RequestRefresh();
                config = await _metadata.GetConfigurationAsync(ct);
                parameters.IssuerSigningKeys = config.SigningKeys;
                result = await _handler.ValidateTokenAsync(token, parameters);
            }
            if (!result.IsValid)
            {
                _logger.LogInformation("Rejected token: {Reason}", result.Exception?.GetType().Name);
                return AuthResult.Fail("Invalid token.");
            }
        }

        var claims = result.ClaimsIdentity;
        var tid = claims.FindFirst("tid")?.Value;
        var oid = claims.FindFirst("oid")?.Value;
        if (!string.Equals(tid, _options.TenantId, StringComparison.OrdinalIgnoreCase)) return AuthResult.Fail("Token is for a different tenant.");
        if (string.IsNullOrEmpty(oid)) return AuthResult.Fail("Token has no user (oid) claim; app-only tokens are not accepted.");
        var scopes = (claims.FindFirst("scp")?.Value ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (!scopes.Contains(_options.RequiredScope, StringComparer.Ordinal)) return AuthResult.Fail($"Token is missing the {_options.RequiredScope} scope.");

        var name = claims.FindFirst("name")?.Value ?? claims.FindFirst("preferred_username")?.Value;
        return AuthResult.Ok(new AuthenticatedUser(tid!.ToLowerInvariant(), oid.ToLowerInvariant(), name));
    }
}
