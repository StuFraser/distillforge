using DistillForge.Blazor;
using DistillForge.Blazor.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Public config only: everything in wwwroot is downloadable by anyone.
var apiBase = builder.Configuration["ApiBaseUrl"]
    ?? throw new InvalidOperationException("ApiBaseUrl missing from wwwroot/appsettings.json");
if (!apiBase.EndsWith('/')) apiBase += "/";

builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(apiBase) });
builder.Services.AddScoped<DistillForgeApi>();
builder.Services.AddScoped<AiSettingsState>();

await builder.Build().RunAsync();
