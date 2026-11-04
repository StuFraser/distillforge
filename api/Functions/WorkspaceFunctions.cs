using DistillForge.Api.Services;
using DistillForge.Core.Contracts;
using DistillForge.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.EntityFrameworkCore;

namespace DistillForge.Api.Functions;

// AuthorizationLevel.Anonymous until Entra ID is wired up (milestone 5).
public sealed class WorkspaceFunctions(DistillForgeDbContext db, WorkspaceService workspaces)
{
    [Function("ListWorkspaces")]
    public async Task<IActionResult> List(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "workspaces")] HttpRequest req,
        CancellationToken ct)
    {
        var items = await db.Workspaces.AsNoTracking()
            .Where(w => !w.IsArchived)
            .OrderByDescending(w => w.UpdatedAt)
            .ToListAsync(ct);
        return new OkObjectResult(items.Select(w => w.ToSummaryDto()));
    }

    [Function("GetWorkspace")]
    public async Task<IActionResult> Get(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "workspaces/{id:guid}")] HttpRequest req,
        Guid id,
        CancellationToken ct)
    {
        var w = await db.Workspaces.AsNoTracking().Include(x => x.Segments)
            .FirstOrDefaultAsync(x => x.Id == id && !x.IsArchived, ct);
        return w is null ? new NotFoundResult() : new OkObjectResult(w.ToDto());
    }

    [Function("CreateWorkspace")]
    public async Task<IActionResult> Create(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "workspaces")] HttpRequest req,
        CancellationToken ct)
    {
        var body = await ReadBody<CreateWorkspaceRequest>(req, ct);
        if (body is null) return new BadRequestObjectResult("Expected JSON body with sourceText.");
        try
        {
            var w = await workspaces.CreateAsync(body.SourceText, body.Title, body.Strategy, ct);
            return new CreatedResult($"/api/workspaces/{w.Id}", w.ToDto());
        }
        catch (SourceValidationException ex)
        {
            return new BadRequestObjectResult(ex.Message);
        }
    }

    [Function("ReplaceWorkspaceSource")]
    public async Task<IActionResult> ReplaceSource(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "workspaces/{id:guid}/source")] HttpRequest req,
        Guid id,
        CancellationToken ct)
    {
        var body = await ReadBody<ReplaceSourceRequest>(req, ct);
        if (body is null) return new BadRequestObjectResult("Expected JSON body with sourceText.");
        try
        {
            var w = await workspaces.ReplaceSourceAsync(id, body.SourceText, body.Strategy, ct);
            return w is null ? new NotFoundResult() : new OkObjectResult(w.ToDto());
        }
        catch (SourceValidationException ex)
        {
            return new BadRequestObjectResult(ex.Message);
        }
    }

    [Function("ListActionItems")]
    public async Task<IActionResult> ListActions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "workspaces/{id:guid}/actions")] HttpRequest req,
        Guid id,
        CancellationToken ct)
    {
        var items = await db.ActionItems.AsNoTracking()
            .Where(a => a.WorkspaceId == id)
            .Include(a => a.Evidence).ThenInclude(e => e.Segment)
            .OrderBy(a => a.SortOrder)
            .ToListAsync(ct);
        return new OkObjectResult(items.Select(a => a.ToDto()));
    }

    internal static async Task<T?> ReadBody<T>(HttpRequest req, CancellationToken ct) where T : class
    {
        try { return await req.ReadFromJsonAsync<T>(ct); }
        catch (System.Text.Json.JsonException) { return null; }
    }
}
