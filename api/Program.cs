using DistillForge.Api.Ai;
using DistillForge.Api.Auth;
using DistillForge.Api.Services;
using DistillForge.Data;
using DistillForge.Data.Security;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

var builder = FunctionsApplication.CreateBuilder(args);
builder.ConfigureFunctionsWebApplication();

// Authentication: every HTTP function requires a valid Entra ID token (default-deny), except in Auth__Mode=Dev (Development only).
var auth = builder.Configuration.GetSection(AuthOptions.Section).Get<AuthOptions>() ?? new AuthOptions();
if (auth.Validate(builder.Environment.IsDevelopment()) is { Count: > 0 } authProblems)
    throw new InvalidOperationException("Authentication is not configured safely: " + string.Join(" ", authProblems));
builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection(AuthOptions.Section));
if (auth.Mode == AuthMode.Dev)
{
    builder.Services.AddSingleton<IRequestAuthenticator, DevAuthenticator>();
}
else
{
    builder.Services.AddSingleton<IConfigurationManager<OpenIdConnectConfiguration>>(_ =>
        new ConfigurationManager<OpenIdConnectConfiguration>(
            auth.ResolvedAuthority + "/.well-known/openid-configuration",
            new OpenIdConnectConfigurationRetriever(), new HttpDocumentRetriever()));
    builder.Services.AddSingleton<IRequestAuthenticator, JwtAuthenticator>();
}
builder.Services.AddScoped<UserProvisioner>();
builder.UseMiddleware<AuthMiddleware>();

var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException("ConnectionStrings__Postgres is not configured (see api/local.settings.sample.json).");
builder.Services.AddDbContext<DistillForgeDbContext>(o => o.UseNpgsql(connectionString));

// Fallback provider from config, used only when no model is configured in the database.
builder.Services.Configure<AiProviderOptions>(builder.Configuration.GetSection(AiProviderOptions.Section));
builder.Services.AddSingleton(new SecretProtector(builder.Configuration["Encryption:Key"] ?? builder.Configuration["Encryption__Key"]));
builder.Services.AddHttpClient();
builder.Services.AddScoped<AiConfigResolver>();
builder.Services.AddSingleton<ActionExtractorFactory>();

builder.Services.AddScoped<WorkspaceService>();
builder.Services.AddSingleton<SourceFileExtractor>();
builder.Services.AddScoped<ExtractionRunner>();

builder.Build().Run();
