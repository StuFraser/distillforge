using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;

namespace DistillForge.Blazor.Services;

/// <summary>Attaches the signed-in user's access token to calls to the API (and only the API), requesting the API scope.</summary>
public sealed class ApiAuthorizationMessageHandler : AuthorizationMessageHandler
{
    public ApiAuthorizationMessageHandler(IAccessTokenProvider provider, NavigationManager navigation, ApiAuthConfig config)
        : base(provider, navigation) => ConfigureHandler(authorizedUrls: [config.ApiBaseUrl], scopes: [config.Scope]);
}

/// <summary>What the handler needs; a plain record so the handler doesn't reach into configuration.</summary>
public sealed record ApiAuthConfig(string ApiBaseUrl, string Scope);
