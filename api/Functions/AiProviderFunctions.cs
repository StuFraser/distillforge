using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using DistillForge.Api.Services;
using DistillForge.Core.Contracts;
using DistillForge.Data;
using DistillForge.Data.Entities;
using DistillForge.Data.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.EntityFrameworkCore;

namespace DistillForge.Api.Functions;

// AuthorizationLevel.Anonymous until Entra ID is wired up (milestone 5): until then anyone who can reach
// the API can USE stored providers (keys are never returned, but tokens are spendable). Gate before deploying.
public sealed class AiProviderFunctions(DistillForgeDbContext db, SecretProtector secrets, IHttpClientFactory httpFactory)
{
    [Function("ListAiProviders")]
    public async Task<IActionResult> List(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "ai/providers")] HttpRequest req, CancellationToken ct)
    {
        var rows = await db.AiProviders.AsNoTracking().Include(p => p.Models).OrderBy(p => p.Name).ToListAsync(ct);
        return new OkObjectResult(rows.Select(p => p.ToDto()));
    }

    [Function("CreateAiProvider")]
    public async Task<IActionResult> Create(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "ai/providers")] HttpRequest req, CancellationToken ct)
    {
        var body = await WorkspaceFunctions.ReadBody<AiProviderRequest>(req, ct);
        if (body is null) return new BadRequestObjectResult("Expected JSON body with name, baseUrl and apiKey.");
        if (AiValidation.ValidateProvider(body) is { } error) return new BadRequestObjectResult(error);
        if (await NameTaken(body.Name, null, ct)) return new ConflictObjectResult($"A provider named '{body.Name.Trim()}' already exists.");

        var p = new AiProvider
        {
            Id = Guid.NewGuid(),
            Name = body.Name.Trim(),
            BaseUrl = AiValidation.NormaliseBaseUrl(body.BaseUrl),
            TimeoutSeconds = body.TimeoutSeconds,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        try { ApplyKey(p, body.ApiKey); }
        catch (SecretProtectionException ex) { return new ObjectResult(ex.Message) { StatusCode = 500 }; }
        db.AiProviders.Add(p);
        await db.SaveChangesAsync(ct);
        return new CreatedResult($"/api/ai/providers/{p.Id}", p.ToDto());
    }

    [Function("UpdateAiProvider")]
    public async Task<IActionResult> Update(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "ai/providers/{id:guid}")] HttpRequest req, Guid id, CancellationToken ct)
    {
        var body = await WorkspaceFunctions.ReadBody<AiProviderRequest>(req, ct);
        if (body is null) return new BadRequestObjectResult("Expected JSON body with name, baseUrl and apiKey.");
        if (AiValidation.ValidateProvider(body) is { } error) return new BadRequestObjectResult(error);

        var p = await db.AiProviders.Include(x => x.Models).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return new NotFoundResult();
        if (await NameTaken(body.Name, id, ct)) return new ConflictObjectResult($"A provider named '{body.Name.Trim()}' already exists.");

        p.Name = body.Name.Trim();
        p.BaseUrl = AiValidation.NormaliseBaseUrl(body.BaseUrl);
        p.TimeoutSeconds = body.TimeoutSeconds;
        try { ApplyKey(p, body.ApiKey); }
        catch (SecretProtectionException ex) { return new ObjectResult(ex.Message) { StatusCode = 500 }; }
        await db.SaveChangesAsync(ct);
        return new OkObjectResult(p.ToDto());
    }

    [Function("DeleteAiProvider")]
    public async Task<IActionResult> Delete(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "ai/providers/{id:guid}")] HttpRequest req, Guid id, CancellationToken ct)
    {
        var p = await db.AiProviders.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return new NotFoundResult();
        db.AiProviders.Remove(p); // models cascade; past runs keep their Model text
        await db.SaveChangesAsync(ct);
        return new NoContentResult();
    }

    [Function("CreateAiModel")]
    public async Task<IActionResult> CreateModel(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "ai/providers/{id:guid}/models")] HttpRequest req, Guid id, CancellationToken ct)
    {
        var body = await WorkspaceFunctions.ReadBody<AiModelRequest>(req, ct);
        if (body is null) return new BadRequestObjectResult("Expected JSON body describing the model.");
        if (AiValidation.ValidateModel(body) is { } error) return new BadRequestObjectResult(error);
        if (!await db.AiProviders.AnyAsync(p => p.Id == id, ct)) return new NotFoundResult();
        if (await db.AiModels.AnyAsync(m => m.AiProviderId == id && m.ModelId == body.ModelId.Trim(), ct))
            return new ConflictObjectResult($"Model '{body.ModelId.Trim()}' is already on this provider.");

        var m = new AiModel { Id = Guid.NewGuid(), AiProviderId = id, ModelId = body.ModelId.Trim() };
        Apply(m, body);
        // The first model ever added becomes the default so extraction works without extra clicks.
        var makeDefault = body.IsDefault || !await db.AiModels.AnyAsync(ct);
        db.AiModels.Add(m);
        await SaveWithDefault(m, makeDefault, ct);
        return new CreatedResult($"/api/ai/models/{m.Id}", m.ToDto());
    }

    [Function("UpdateAiModel")]
    public async Task<IActionResult> UpdateModel(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "ai/models/{id:guid}")] HttpRequest req, Guid id, CancellationToken ct)
    {
        var body = await WorkspaceFunctions.ReadBody<AiModelRequest>(req, ct);
        if (body is null) return new BadRequestObjectResult("Expected JSON body describing the model.");
        if (AiValidation.ValidateModel(body) is { } error) return new BadRequestObjectResult(error);

        var m = await db.AiModels.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (m is null) return new NotFoundResult();
        if (await db.AiModels.AnyAsync(x => x.AiProviderId == m.AiProviderId && x.ModelId == body.ModelId.Trim() && x.Id != id, ct))
            return new ConflictObjectResult($"Model '{body.ModelId.Trim()}' is already on this provider.");

        m.ModelId = body.ModelId.Trim();
        Apply(m, body);
        // Un-ticking "default" on the current default is ignored: pick another model as default instead.
        await SaveWithDefault(m, body.IsDefault || m.IsDefault, ct);
        return new OkObjectResult(m.ToDto());
    }

    [Function("DeleteAiModel")]
    public async Task<IActionResult> DeleteModel(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "ai/models/{id:guid}")] HttpRequest req, Guid id, CancellationToken ct)
    {
        var m = await db.AiModels.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (m is null) return new NotFoundResult();
        var wasDefault = m.IsDefault;
        db.AiModels.Remove(m);
        await db.SaveChangesAsync(ct);
        if (wasDefault && await db.AiModels.OrderBy(x => x.ModelId).FirstOrDefaultAsync(ct) is { } next)
        {
            next.IsDefault = true;
            await db.SaveChangesAsync(ct);
        }
        return new NoContentResult();
    }

    /// <summary>Flat list for the "which model for this extraction" dropdown.</summary>
    [Function("ListAiModels")]
    public async Task<IActionResult> ListModels(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "ai/models")] HttpRequest req, CancellationToken ct)
    {
        var rows = await db.AiModels.AsNoTracking().Include(m => m.Provider).ToListAsync(ct);
        return new OkObjectResult(rows
            .OrderByDescending(m => m.IsDefault).ThenBy(m => m.Provider!.Name).ThenBy(m => m.ModelId)
            .Select(m => new AiModelChoiceDto(m.Id, AiValidation.ModelLabel(m.Provider!.Name, m.ModelId, m.DisplayName), m.IsDefault)));
    }

    /// <summary>
    /// Asks an endpoint for its models (standard GET {base}/models). Doubles as "Test connection".
    /// Always returns 200 with Ok=false and a message, so the UI can show what went wrong.
    /// </summary>
    [Function("DiscoverAiModels")]
    public async Task<IActionResult> Discover(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "ai/discover")] HttpRequest req, CancellationToken ct)
    {
        var body = await WorkspaceFunctions.ReadBody<DiscoverModelsRequest>(req, ct);
        if (body is null) return new BadRequestObjectResult("Expected JSON body with baseUrl.");
        if (AiValidation.BaseUrlError(body.BaseUrl) is { } urlError) return new OkObjectResult(new DiscoverModelsResponse(false, [], urlError));

        string? key = body.ApiKey;
        if (key is null && body.ProviderId is Guid pid)
        {
            var stored = await db.AiProviders.AsNoTracking().Where(p => p.Id == pid).Select(p => p.ApiKeyEncrypted).FirstOrDefaultAsync(ct);
            try { if (!string.IsNullOrEmpty(stored)) key = secrets.Unprotect(stored); }
            catch (SecretProtectionException ex) { return new OkObjectResult(new DiscoverModelsResponse(false, [], ex.Message)); }
        }

        try
        {
            var http = httpFactory.CreateClient("ai");
            http.Timeout = TimeSpan.FromSeconds(15);
            using var request = new HttpRequestMessage(HttpMethod.Get, AiValidation.NormaliseBaseUrl(body.BaseUrl) + "/models");
            if (!string.IsNullOrEmpty(key)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            using var response = await http.SendAsync(request, ct);
            var raw = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
                return new OkObjectResult(new DiscoverModelsResponse(false, [], $"The endpoint answered {(int)response.StatusCode} {response.ReasonPhrase}. Check the URL and API key."));

            var ids = (JsonNode.Parse(raw)?["data"] as JsonArray)?
                .Select(n => n?["id"]?.GetValue<string>())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s!)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToList();
            return ids is null
                ? new OkObjectResult(new DiscoverModelsResponse(true, [], "Connected, but the endpoint doesn't list its models; type the model ID by hand."))
                : new OkObjectResult(new DiscoverModelsResponse(true, ids, null));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or InvalidOperationException)
        {
            return new OkObjectResult(new DiscoverModelsResponse(false, [], $"Couldn't reach the endpoint: {ex.Message}"));
        }
    }

    private async Task<bool> NameTaken(string name, Guid? exceptId, CancellationToken ct)
    {
        var n = name.Trim().ToLower();
        return await db.AiProviders.AnyAsync(p => p.Name.ToLower() == n && p.Id != exceptId, ct);
    }

    private void ApplyKey(AiProvider p, string? apiKey)
    {
        if (apiKey is null) return;                       // keep
        p.ApiKeyEncrypted = apiKey.Trim().Length == 0 ? null : secrets.Protect(apiKey.Trim());
    }

    private static void Apply(AiModel m, AiModelRequest r)
    {
        m.DisplayName = string.IsNullOrWhiteSpace(r.DisplayName) ? null : r.DisplayName.Trim();
        m.ContextTokens = r.ContextTokens;
        m.ReserveOutput = r.ReserveOutput;
        m.ResponseFormat = r.ResponseFormat;
    }

    /// <summary>Saves, making <paramref name="m"/> the one default when asked. Clears the old default first (unique index).</summary>
    private async Task SaveWithDefault(AiModel m, bool makeDefault, CancellationToken ct)
    {
        if (!makeDefault) { await db.SaveChangesAsync(ct); return; }
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.AiModels.Where(x => x.IsDefault && x.Id != m.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsDefault, false), ct);
        m.IsDefault = true;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }
}
