using DistillForge.Api.Ai;
using DistillForge.Api.Services;
using DistillForge.Core.Extraction;
using DistillForge.Data;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

var builder = FunctionsApplication.CreateBuilder(args);
builder.ConfigureFunctionsWebApplication();

var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException("ConnectionStrings__Postgres is not configured (see api/local.settings.sample.json).");
builder.Services.AddDbContext<DistillForgeDbContext>(o => o.UseNpgsql(connectionString));

builder.Services.Configure<AiProviderOptions>(builder.Configuration.GetSection(AiProviderOptions.Section));
builder.Services.AddHttpClient<IActionExtractor, OpenAiCompatibleActionExtractor>((sp, http) =>
{
    var opt = sp.GetRequiredService<IOptions<AiProviderOptions>>().Value;
    http.Timeout = TimeSpan.FromSeconds(opt.TimeoutSeconds);
});

builder.Services.AddScoped<WorkspaceService>();
builder.Services.AddScoped<ExtractionRunner>();

builder.Build().Run();
