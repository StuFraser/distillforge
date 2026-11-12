using DistillForge.Api.Ai;
using DistillForge.Api.Services;
using DistillForge.Data;
using DistillForge.Data.Security;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = FunctionsApplication.CreateBuilder(args);
builder.ConfigureFunctionsWebApplication();

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
