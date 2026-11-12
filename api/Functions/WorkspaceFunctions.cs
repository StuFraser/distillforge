using DistillForge.Api.Services;
using DistillForge.Core.Contracts;
using DistillForge.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.EntityFrameworkCore;

namespace DistillForge.Api.Functions;

// AuthorizationLevel.Anonymous until Entra ID is wired up (milestone 5).
public sealed class WorkspaceFunctions(DistillForgeDbContext db, WorkspaceService workspaces, SourceFileExtractor sourceFiles)
{
    [Function("ExtractSourceFile")]
    public async Task<IActionResult> ExtractSourceFile(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "sources/extract")] HttpRequest req,
        CancellationToken ct)
    {
        if (!req.HasFormContentType) return new BadRequestObjectResult("Expected a multipart form with a file.");
        var form = await req.ReadFormAsync(ct);
        var file = form.Files.GetFile("file");
        if (file is null || file.Length == 0) return new BadRequestObjectResult("Choose a non-empty file.");
        if (file.Length > SourceFileExtractor.MaxUploadBytes)
            return new BadRequestObjectResult("That file is too large. The upload limit is 10 MB.");

        try
        {
            await using var stream = file.OpenReadStream();
            var text = await sourceFiles.ExtractAsync(stream, file.FileName, ct);
            if (string.IsNullOrWhiteSpace(text)) return new BadRequestObjectResult("No text could be extracted from that file.");
            return new OkObjectResult(new ExtractSourceTextResponse(file.FileName, text));
        }
        catch (SourceFileException ex)
        {
            return new BadRequestObjectResult(ex.Message);
        }
    }

    [Function("ListWorkspaces")]
    public async Task<IActionResult> List(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "workspaces")] HttpRequest req,
        CancellationToken ct)
    {
        // Archived workspaces are hidden unless asked for: GET /api/workspaces?includeArchived=true
        var includeArchived = bool.TryParse(req.Query["includeArchived"], out var flag) && flag;
        var rows = await db.Workspaces.AsNoTracking()
            .Where(w => includeArchived || !w.IsArchived)
            .OrderByDescending(w => w.UpdatedAt)
            .Select(w => new { Workspace = w, Count = w.ActionItems.Count(a => a.MergedIntoId == null) })
            .ToListAsync(ct);
        return new OkObjectResult(rows.Select(r => r.Workspace.ToSummaryDto(r.Count)));
    }

    [Function("GetWorkspace")]
    public async Task<IActionResult> Get(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "workspaces/{id:guid}")] HttpRequest req,
        Guid id,
        CancellationToken ct)
    {
        var w = await db.Workspaces.AsNoTracking().Include(x => x.Segments)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
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

    [Function("ArchiveWorkspace")]
    public Task<IActionResult> Archive(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "workspaces/{id:guid}/archive")] HttpRequest req,
        Guid id, CancellationToken ct) => SetArchived(id, true, ct);

    [Function("UnarchiveWorkspace")]
    public Task<IActionResult> Unarchive(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "workspaces/{id:guid}/unarchive")] HttpRequest req,
        Guid id, CancellationToken ct) => SetArchived(id, false, ct);

    [Function("ListWorkspaceRuns")]
    public async Task<IActionResult> ListRuns(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "workspaces/{id:guid}/runs")] HttpRequest req,
        Guid id,
        CancellationToken ct)
    {
        var runs = await db.ExtractionRuns.AsNoTracking()
            .Where(r => r.WorkspaceId == id)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(ct);
        return new OkObjectResult(runs.Select(r => r.ToDto()));
    }

    [Function("ListActionItems")]
    public async Task<IActionResult> ListActions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "workspaces/{id:guid}/actions")] HttpRequest req,
        Guid id,
        CancellationToken ct)
    {
        return new OkObjectResult(await ActionItemQueries.LoadDtosAsync(db, id, ct));
    }

    private async Task<IActionResult> SetArchived(Guid id, bool archived, CancellationToken ct)
    {
        // Deliberately leaves UpdatedAt alone: archiving is housekeeping, not an edit.
        var w = await db.Workspaces.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (w is null) return new NotFoundResult();
        if (w.IsArchived != archived)
        {
            w.IsArchived = archived;
            await db.SaveChangesAsync(ct);
        }
        return new OkObjectResult(w.ToSummaryDto(await db.ActionItems.CountAsync(a => a.WorkspaceId == id && a.MergedIntoId == null, ct)));
    }

    internal static async Task<T?> ReadBody<T>(HttpRequest req, CancellationToken ct) where T : class
    {
        try { return await req.ReadFromJsonAsync<T>(ct); }
        // InvalidOperationException: no/unknown content type (e.g. a bodyless POST).
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException) { return null; }
    }
}
