using DistillForge.Blazor;
using DistillForge.Blazor.Services;
using DistillForge.Core.Auth;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Public config only: everything in wwwroot is downloadable by anyone.
var apiBase = builder.Configuration["ApiBaseUrl"]
    ?? throw new InvalidOperationException("ApiBaseUrl missing from wwwroot/appsettings.json");
if (!apiBase.EndsWith('/')) apiBase += "/";

var auth = builder.Configuration.GetSection(ClientAuthSettings.Section).Get<ClientAuthSettings>() ?? new ClientAuthSettings();
if (auth.Validate() is { Count: > 0 } problems)
    throw new InvalidOperationException("Sign-in is half configured in wwwroot/appsettings.json: " + string.Join(" ", problems) +
        " Fill in the whole \"Auth\" section (see design-notes/entraId_provisioning.md), or leave it empty for dev mode.");
builder.Services.AddSingleton(auth);
builder.Services.AddSingleton<AuthSettingsHolder>();

if (auth.IsEnabled)
{
    // Entra ID sign-in (authorization code + PKCE, via MSAL). Every API call carries the user's access token.
    builder.Services.AddMsalAuthentication(options =>
    {
        options.ProviderOptions.Authentication.Authority = auth.ResolvedAuthority;
        options.ProviderOptions.Authentication.ClientId = auth.ClientId!;
        options.ProviderOptions.DefaultAccessTokenScopes.Add(auth.ApiScope!);
        options.ProviderOptions.LoginMode = "redirect";
    });
    builder.Services.AddSingleton(new ApiAuthConfig(apiBase, auth.ApiScope!));
    builder.Services.AddTransient<ApiAuthorizationMessageHandler>();
    builder.Services.AddHttpClient("api", c => c.BaseAddress = new Uri(apiBase))
        .AddHttpMessageHandler<ApiAuthorizationMessageHandler>();
    builder.Services.AddScoped(sp => sp.GetRequiredService<IHttpClientFactory>().CreateClient("api"));
}
else
{
    // Dev mode: a fixed fake user and plain, token-less calls (the API must be running with Auth__Mode=Dev).
    builder.Services.AddAuthorizationCore();
    builder.Services.AddScoped<AuthenticationStateProvider, DevAuthenticationStateProvider>();
    builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(apiBase) });
}

builder.Services.AddScoped<DistillForgeApi>();
builder.Services.AddScoped<AiSettingsState>();
builder.Services.AddScoped<DialogService>();

await builder.Build().RunAsync();
