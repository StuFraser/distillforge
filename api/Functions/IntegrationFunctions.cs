using System.Text.Json;
using DistillForge.Api.Services;
using DistillForge.Core.Integrations;
using DistillForge.Data;
using DistillForge.Data.Entities;
using DistillForge.Data.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.EntityFrameworkCore;

namespace DistillForge.Api.Functions;

// Connections to external platforms belong to the signed-in user (query filter on Integration; see AuthMiddleware).
// Secret fields are encrypted as one JSON blob and never returned. Pushing notes to a platform is a separate piece.
public sealed class IntegrationFunctions(DistillForgeDbContext db, SecretProtector secrets)
{
    [Function("ListIntegrations")]
    public async Task<IActionResult> List(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "integrations")] HttpRequest req, CancellationToken ct)
    {
        var rows = await db.Integrations.AsNoTracking().ToListAsync(ct);
        return new OkObjectResult(rows.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase).Select(i => i.ToDto()));
    }

    [Function("CreateIntegration")]
    public async Task<IActionResult> Create(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "integrations")] HttpRequest req, CancellationToken ct)
    {
        var body = await WorkspaceFunctions.ReadBody<IntegrationRequest>(req, ct);
        if (body is null) return new BadRequestObjectResult("Expected JSON body with platform, name and values.");
        if (IntegrationValidation.Validate(body) is { } error) return new BadRequestObjectResult(error);
        if (await NameTaken(body.Name, null, ct)) return new ConflictObjectResult($"A connection named '{body.Name.Trim()}' already exists.");

        var i = new Integration { Id = Guid.NewGuid(), Platform = body.Platform, Name = body.Name.Trim(), CreatedAt = DateTimeOffset.UtcNow };
        try { Apply(i, body, []); }
        catch (SecretProtectionException ex) { return new ObjectResult(ex.Message) { StatusCode = 500 }; }
        db.Integrations.Add(i);
        await db.SaveChangesAsync(ct);
        return new CreatedResult($"/api/integrations/{i.Id}", i.ToDto());
    }

    [Function("UpdateIntegration")]
    public async Task<IActionResult> Update(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "integrations/{id:guid}")] HttpRequest req, Guid id, CancellationToken ct)
    {
        var body = await WorkspaceFunctions.ReadBody<IntegrationRequest>(req, ct);
        if (body is null) return new BadRequestObjectResult("Expected JSON body with platform, name and values.");

        var i = await db.Integrations.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (i is null) return new NotFoundResult();
        if (body.Platform != i.Platform) return new BadRequestObjectResult("A connection's platform can't be changed; add a new connection instead.");
        if (IntegrationValidation.Validate(body, i.SecretKeys) is { } error) return new BadRequestObjectResult(error);
        if (await NameTaken(body.Name, id, ct)) return new ConflictObjectResult($"A connection named '{body.Name.Trim()}' already exists.");

        Dictionary<string, string> current;
        try { current = Decrypt(i); }
        catch (SecretProtectionException ex) { return new ObjectResult(ex.Message) { StatusCode = 500 }; }

        i.Name = body.Name.Trim();
        try { Apply(i, body, current); }
        catch (SecretProtectionException ex) { return new ObjectResult(ex.Message) { StatusCode = 500 }; }
        await db.SaveChangesAsync(ct);
        return new OkObjectResult(i.ToDto());
    }

    [Function("DeleteIntegration")]
    public async Task<IActionResult> Delete(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "integrations/{id:guid}")] HttpRequest req, Guid id, CancellationToken ct)
    {
        var i = await db.Integrations.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (i is null) return new NotFoundResult();
        db.Integrations.Remove(i);
        await db.SaveChangesAsync(ct);
        return new NoContentResult();
    }

    private async Task<bool> NameTaken(string name, Guid? exceptId, CancellationToken ct)
    {
        var n = name.Trim().ToLower();
        return await db.Integrations.AnyAsync(x => x.Name.ToLower() == n && x.Id != exceptId, ct);
    }

    private Dictionary<string, string> Decrypt(Integration i) =>
        string.IsNullOrEmpty(i.SecretsEncrypted)
            ? []
            : JsonSerializer.Deserialize<Dictionary<string, string>>(secrets.Unprotect(i.SecretsEncrypted)) ?? [];

    /// <summary>Writes settings and merges secrets (absent/null keep, "" clear, otherwise replace) onto <paramref name="current"/>.</summary>
    private void Apply(Integration i, IntegrationRequest r, Dictionary<string, string> current)
    {
        i.SettingsJson = JsonSerializer.Serialize(IntegrationValidation.CleanSettings(r));

        var merged = new Dictionary<string, string>(current);
        foreach (var f in IntegrationCatalog.Get(r.Platform).Fields.Where(f => f.IsSecret))
        {
            if (!r.Values.TryGetValue(f.Key, out var v) || v is null) continue;
            if (v.Trim().Length == 0) merged.Remove(f.Key); else merged[f.Key] = v.Trim();
        }
        i.SecretKeys = merged.Keys.Order().ToList();
        i.SecretsEncrypted = merged.Count == 0 ? null : secrets.Protect(JsonSerializer.Serialize(merged));
    }
}
