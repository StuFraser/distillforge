using DistillForge.Data;
using DistillForge.Data.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;

namespace DistillForge.Api.Ai;

/// <summary>
/// Turns "which model?" into a concrete <see cref="AiProviderOptions"/>:
/// the requested model, else the default model, else the AiProvider__* config section.
/// </summary>
public sealed class AiConfigResolver(DistillForgeDbContext db, SecretProtector secrets, IOptions<AiProviderOptions> fallback)
{
    public async Task<AiProviderOptions> ResolveAsync(Guid? aiModelId, CancellationToken ct)
    {
        var query = db.AiModels.AsNoTracking().Include(m => m.Provider);
        var model = aiModelId is Guid id
            ? await query.FirstOrDefaultAsync(m => m.Id == id, ct)
            : null;
        // A requested model that has since been deleted falls back to the default rather than failing the run.
        model ??= await query.FirstOrDefaultAsync(m => m.IsDefault, ct);

        if (model?.Provider is { } p)
        {
            return new AiProviderOptions
            {
                BaseUrl = p.BaseUrl,
                Model = model.ModelId,
                ApiKey = string.IsNullOrEmpty(p.ApiKeyEncrypted) ? null : secrets.Unprotect(p.ApiKeyEncrypted),
                ContextTokens = model.ContextTokens,
                ReserveOutput = model.ReserveOutput,
                ResponseFormat = model.ResponseFormat,
                TimeoutSeconds = p.TimeoutSeconds,
                SafetyMargin = fallback.Value.SafetyMargin,
            };
        }

        var cfg = fallback.Value;
        if (string.IsNullOrWhiteSpace(cfg.BaseUrl) || string.IsNullOrWhiteSpace(cfg.Model))
            throw new ExtractionFailedException("No AI model is configured. Add a provider and model in AI settings (the ⚙ button).");
        return cfg;
    }
}

/// <summary>Builds an extractor for a resolved config. The HttpClient timeout is per provider, so each extractor gets its own client.</summary>
public sealed class ActionExtractorFactory(IHttpClientFactory httpFactory, ILoggerFactory loggerFactory)
{
    public OpenAiCompatibleActionExtractor Create(AiProviderOptions options)
    {
        var http = httpFactory.CreateClient("ai");
        http.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        return new OpenAiCompatibleActionExtractor(http, options, loggerFactory.CreateLogger<OpenAiCompatibleActionExtractor>());
    }
}
