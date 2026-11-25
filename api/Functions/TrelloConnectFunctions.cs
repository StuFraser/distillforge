using System.Text.Json;
using DistillForge.Api.Export;
using DistillForge.Api.Services;
using DistillForge.Core.Integrations;
using DistillForge.Data;
using DistillForge.Data.Entities;
using DistillForge.Data.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DistillForge.Api.Functions;

/// <summary>"Connect with Trello": completes the OAuth sign-in and stores the user's tokens. The tokens never go to the browser.</summary>
public sealed class TrelloConnectFunctions(DistillForgeDbContext db, SecretProtector secrets, TrelloOAuthClient oauth, IOptions<TrelloOptions> options)
{
    [Function("GetTrelloConnectInfo")]
    public IActionResult Info([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "integrations/trello/connect-info")] HttpRequest req)
    {
        var id = options.Value.ClientId?.Trim();
        return new OkObjectResult(string.IsNullOrEmpty(id) ? new TrelloConnectInfoDto(false, null) : new TrelloConnectInfoDto(true, id));
    }

    [Function("ConnectTrello")]
    public async Task<IActionResult> Connect(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "integrations/trello/connect")] HttpRequest req, CancellationToken ct)
    {
        var body = await WorkspaceFunctions.ReadBody<TrelloConnectRequest>(req, ct);
        if (body is null) return new BadRequestObjectResult("Expected JSON body with name, code, codeVerifier and redirectUri.");
        if (!oauth.Configured) return new BadRequestObjectResult("Trello isn't set up on this server (Trello__ClientId is missing).");
        if (Problem(body) is { } problem) return new BadRequestObjectResult(problem);

        var name = body.Name.Trim();
        Integration? existing = null;
        if (body.IntegrationId is Guid id)
        {
            existing = await db.Integrations.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (existing is null) return new NotFoundResult();
            if (existing.Platform != IntegrationPlatform.Trello) return new BadRequestObjectResult("That isn't a Trello connection.");
        }
        var lower = name.ToLower();
        if (await db.Integrations.AnyAsync(x => x.Name.ToLower() == lower && x.Id != body.IntegrationId, ct))
            return new ConflictObjectResult($"A connection named '{name}' already exists.");

        TrelloTokens tokens;
        try { tokens = await oauth.ExchangeCodeAsync(body.Code.Trim(), body.CodeVerifier.Trim(), body.RedirectUri.Trim(), ct); }
        catch (TrelloOAuthException ex) { return new BadRequestObjectResult(ex.Message); }

        try
        {
            var protectedSecrets = secrets.Protect(JsonSerializer.Serialize(TrelloTarget.ToSecrets(tokens)));
            var keys = TrelloTarget.ToSecrets(tokens).Keys.Order().ToList();
            if (existing is null)
            {
                existing = new Integration { Id = Guid.NewGuid(), Platform = IntegrationPlatform.Trello, Name = name, CreatedAt = DateTimeOffset.UtcNow };
                db.Integrations.Add(existing);
            }
            existing.Name = name;
            existing.SettingsJson = "{}";
            existing.SecretsEncrypted = protectedSecrets;
            existing.SecretKeys = keys;
        }
        catch (SecretProtectionException ex) { return new ObjectResult(ex.Message) { StatusCode = 500 }; }

        await db.SaveChangesAsync(ct);
        return body.IntegrationId is null ? new CreatedResult($"/api/integrations/{existing.Id}", existing.ToDto()) : new OkObjectResult(existing.ToDto());
    }

    private static string? Problem(TrelloConnectRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.Name)) return "Name is required.";
        if (r.Name.Trim().Length > IntegrationValidation.MaxName) return $"Name is too long (max {IntegrationValidation.MaxName} characters).";
        if (string.IsNullOrWhiteSpace(r.Code) || r.Code.Length > 2048) return "Trello didn't send an approval code. Try connecting again.";
        if (string.IsNullOrWhiteSpace(r.CodeVerifier) || r.CodeVerifier.Length is < 43 or > 128) return "The sign-in was interrupted. Try connecting again.";
        // Only the app's own callback page is ever a valid redirect; Atlassian also checks it against the registered callback URLs.
        if (!Uri.TryCreate(r.RedirectUri, UriKind.Absolute, out var u) || u.Scheme is not ("http" or "https")
            || !u.AbsolutePath.EndsWith("/" + TrelloAuth.CallbackPage, StringComparison.Ordinal) || !string.IsNullOrEmpty(u.Query) || !string.IsNullOrEmpty(u.Fragment))
            return "That isn't a valid callback address.";
        return null;
    }
}
