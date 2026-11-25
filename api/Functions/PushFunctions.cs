using System.Text.Json;
using DistillForge.Api.Export;
using DistillForge.Core.Codes;
using DistillForge.Core.Export;
using DistillForge.Data;
using DistillForge.Data.Entities;
using DistillForge.Data.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.EntityFrameworkCore;

namespace DistillForge.Api.Functions;

// Pushing notes to a saved connection. Notes are loaded here from their ids (owner-filtered), so a client can only push content it owns.
public sealed class PushFunctions(DistillForgeDbContext db, SecretProtector secrets, IEnumerable<IExportTarget> targets)
{
    [Function("GetPushOptions")]
    public async Task<IActionResult> Options(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "integrations/{id:guid}/push-options")] HttpRequest req, Guid id, CancellationToken ct)
    {
        var resolved = await Resolve(id, ct);
        if (resolved.Error is not null) return resolved.Error;
        return new OkObjectResult(await resolved.Target!.GetOptionsAsync(resolved.Connection!, req.Query["scope"].ToString(), ct));
    }

    [Function("PushNotes")]
    public async Task<IActionResult> Push(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "integrations/{id:guid}/push")] HttpRequest req, Guid id, CancellationToken ct)
    {
        var body = await WorkspaceFunctions.ReadBody<PushRequest>(req, ct);
        if (body is null) return new BadRequestObjectResult("Expected JSON body with workspaceId, noteIds and itemType.");
        var noteIds = body.NoteIds?.Distinct().ToList() ?? [];
        if (noteIds.Count == 0) return new BadRequestObjectResult("Choose at least one note.");
        if (noteIds.Count > PushSupport.MaxNotes) return new BadRequestObjectResult($"Push at most {PushSupport.MaxNotes} notes at a time.");
        if (string.IsNullOrWhiteSpace(body.ItemType) || body.ItemType.Length > 200) return new BadRequestObjectResult("Choose what to create (e.g. Task).");

        var resolved = await Resolve(id, ct);
        if (resolved.Error is not null) return resolved.Error;

        var scope = string.IsNullOrWhiteSpace(body.Scope) ? null : body.Scope.Trim();
        if (resolved.Target!.ScopeLabel is { } label && scope is null) return new BadRequestObjectResult($"Choose a {label.ToLowerInvariant()}.");
        if (scope is { Length: > 200 } || body.AreaPath is { Length: > 500 }) return new BadRequestObjectResult("The project or area path is too long.");
        var choices = new PushChoices(body.ItemType.Trim(), scope, string.IsNullOrWhiteSpace(body.AreaPath) ? null : body.AreaPath.Trim());

        var workspace = await db.Workspaces.AsNoTracking().FirstOrDefaultAsync(w => w.Id == body.WorkspaceId, ct);
        if (workspace is null) return new NotFoundResult();

        var notes = (await db.ActionItems
            .Include(a => a.Category).Include(a => a.Evidence).ThenInclude(e => e.Segment)
            .Where(a => a.WorkspaceId == workspace.Id && a.MergedIntoId == null && noteIds.Contains(a.Id))
            .ToListAsync(ct)).ToDictionary(a => a.Id);
        var already = (await db.NotePushes.Where(p => p.IntegrationId == id && p.Scope == scope && noteIds.Contains(p.ActionItemId)).ToListAsync(ct))
            .GroupBy(p => p.ActionItemId).ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.PushedAt).First());

        var outcomes = new List<PushOutcome>();
        string? stopped = null;
        foreach (var noteId in noteIds)
        {
            if (!notes.TryGetValue(noteId, out var note)) { outcomes.Add(Fail(noteId, "That note no longer exists, or was merged into another.")); continue; }
            if (stopped is not null) { outcomes.Add(Fail(noteId, "Not attempted: " + stopped)); continue; }
            if (!body.Repush && already.TryGetValue(noteId, out var prior))
            {
                outcomes.Add(new(noteId, false, prior.ExternalId, prior.ExternalUrl, "Already pushed."));
                continue;
            }

            try
            {
                var outcome = await resolved.Target!.PushAsync(resolved.Connection!, ToItem(note, workspace, body.IncludeSource), choices, ct);
                outcomes.Add(outcome);
                if (outcome.Ok)
                    db.NotePushes.Add(new NotePush
                    {
                        Id = Guid.NewGuid(), ActionItemId = noteId, IntegrationId = id, ExternalId = outcome.ExternalId!,
                        ExternalUrl = outcome.Url, ItemType = choices.ItemType, Scope = scope, PushedAt = DateTimeOffset.UtcNow,
                    });
            }
            catch (ExportAuthException ex)
            {
                stopped = ex.Message;
                outcomes.Add(Fail(noteId, ex.Message));
            }
        }
        await db.SaveChangesAsync(ct);   // keep records of what did get created even if later notes failed
        return new OkObjectResult(new PushResponse(outcomes));
    }

    [Function("ListWorkspacePushes")]
    public async Task<IActionResult> ListPushes(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "workspaces/{id:guid}/pushes")] HttpRequest req, Guid id, CancellationToken ct)
    {
        if (!await db.Workspaces.AnyAsync(w => w.Id == id, ct)) return new NotFoundResult();
        var noteIds = db.ActionItems.Where(a => a.WorkspaceId == id).Select(a => a.Id);
        var rows = await db.NotePushes.AsNoTracking().Where(p => noteIds.Contains(p.ActionItemId)).ToListAsync(ct);
        return new OkObjectResult(rows.OrderByDescending(p => p.PushedAt)
            .Select(p => new NotePushDto(p.ActionItemId, p.IntegrationId, p.ExternalId, p.ExternalUrl, p.ItemType, p.PushedAt, p.Scope)));
    }

    private static PushOutcome Fail(Guid noteId, string error) => new(noteId, false, null, null, error);

    private static ExportItem ToItem(ActionItem n, Workspace workspace, bool includeSource)
    {
        var first = includeSource ? n.Evidence.OrderBy(e => e.Segment?.Ordinal ?? int.MaxValue).ThenBy(e => e.Start).FirstOrDefault() : null;
        return new(n.Id, n.Title, n.Summary, n.Owner, n.Due, first?.Quote, n.Category?.Name, workspace.Title,
            ShortCode.ForNote(n.Code), ShortCode.ForWorkspace(workspace.Code), workspace.CreatedAt);
    }

    private sealed record Resolved(IExportTarget? Target, TargetConnection? Connection, IActionResult? Error);

    private async Task<Resolved> Resolve(Guid id, CancellationToken ct)
    {
        var i = await db.Integrations.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (i is null) return new(null, null, new NotFoundResult());
        var target = targets.FirstOrDefault(t => t.Platform == i.Platform);
        if (target is null) return new(null, null, new BadRequestObjectResult($"Pushing to {i.Platform} isn't supported yet."));
        try
        {
            var settings = JsonSerializer.Deserialize<Dictionary<string, string>>(i.SettingsJson) ?? [];
            var secretValues = string.IsNullOrEmpty(i.SecretsEncrypted)
                ? []
                : JsonSerializer.Deserialize<Dictionary<string, string>>(secrets.Unprotect(i.SecretsEncrypted)) ?? [];
            var connection = new TargetConnection(settings, secretValues);

            // Expiring credentials (OAuth) are renewed here and the new ones saved, so every caller gets a usable connection.
            if (await target.RefreshSecretsAsync(connection, ct) is { } renewed)
            {
                i.SecretsEncrypted = secrets.Protect(JsonSerializer.Serialize(renewed));
                i.SecretKeys = renewed.Keys.Order().ToList();
                await db.SaveChangesAsync(ct);
                connection = new TargetConnection(settings, renewed);
            }
            return new(target, connection, null);
        }
        catch (SecretProtectionException ex) { return new(null, null, new ObjectResult(ex.Message) { StatusCode = 500 }); }
        catch (ExportAuthException ex) { return new(null, null, new BadRequestObjectResult(ex.Message)); }
    }
}
