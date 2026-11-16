using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace DistillForge.Blazor.Services;

/// <summary>
/// Dev mode (no Auth settings in wwwroot/appsettings.json): always "signed in" as a fixed user and no token is sent, which is
/// what the API expects with Auth__Mode=Dev. Never registered when real sign-in is configured.
/// </summary>
public sealed class DevAuthenticationStateProvider : AuthenticationStateProvider
{
    public const string UserName = "Dev User";

    public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
        Task.FromResult(new AuthenticationState(new ClaimsPrincipal(
            new ClaimsIdentity([new Claim("name", UserName)], authenticationType: "dev", nameType: "name", roleType: null))));
}
