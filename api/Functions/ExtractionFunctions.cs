using DistillForge.Api.Services;
using DistillForge.Core.Contracts;
using DistillForge.Data;
using DistillForge.Data.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.EntityFrameworkCore;

namespace DistillForge.Api.Functions;

/// <summary>
/// Extraction is async: model calls can outlast Azure's ~230s HTTP limit, so the HTTP call
/// queues a run and returns 202, a queue trigger does the work, and the client polls the run.
/// </summary>
public sealed class ExtractionFunctions(DistillForgeDbContext db, ExtractionRunner runner)
{
    public sealed class StartExtractionOutput
    {
        [HttpResult]
        public required IActionResult Result { get; init; }

        [QueueOutput(QueueNames.ExtractionRuns)]
        public string? RunId { get; init; }
    }

    [Function("StartExtraction")]
    public async Task<StartExtractionOutput> Start(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "workspaces/{id:guid}/extract")] HttpRequest req,
        Guid id,
        CancellationToken ct)
    {
        if (!await db.Workspaces.AnyAsync(w => w.Id == id && !w.IsArchived, ct))
            return new StartExtractionOutput { Result = new NotFoundResult() };

        // Optional body { aiModelId }. No body (or null id) = the workspace's last model, else the default.
        var request = await WorkspaceFunctions.ReadBody<StartExtractionRequest>(req, ct);
        var modelId = request?.AiModelId;
        if (modelId is Guid requested && !await db.AiModels.AnyAsync(m => m.Id == requested, ct))
            return new StartExtractionOutput { Result = new BadRequestObjectResult("That AI model no longer exists.") };
        modelId ??= await db.ExtractionRuns.AsNoTracking()
            .Where(r => r.WorkspaceId == id && r.AiModelId != null)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => r.AiModelId)
            .FirstOrDefaultAsync(ct);

        var active = await db.ExtractionRuns.AsNoTracking()
            .Where(r => r.WorkspaceId == id && (r.Status == ExtractionRunStatus.Queued || r.Status == ExtractionRunStatus.Running))
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync(ct);
        if (active is not null)
            return new StartExtractionOutput { Result = new ConflictObjectResult(active.ToDto()) };

        var run = new ExtractionRun
        {
            Id = Guid.CreateVersion7(),
            WorkspaceId = id,
            Status = ExtractionRunStatus.Queued,
            AiModelId = modelId,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.ExtractionRuns.Add(run);
        await db.SaveChangesAsync(ct);

        return new StartExtractionOutput
        {
            Result = new AcceptedResult($"/api/runs/{run.Id}", run.ToDto()),
            RunId = run.Id.ToString(),
        };
    }

    [Function("GetExtractionRun")]
    public async Task<IActionResult> GetRun(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "runs/{id:guid}")] HttpRequest req,
        Guid id,
        CancellationToken ct)
    {
        var run = await db.ExtractionRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
        return run is null ? new NotFoundResult() : new OkObjectResult(run.ToDto());
    }

    /// <summary>Stops a queued or running extraction. Idempotent; a finished run is returned unchanged.</summary>
    [Function("CancelExtraction")]
    public async Task<IActionResult> Cancel(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "runs/{id:guid}/cancel")] HttpRequest req,
        Guid id,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        await db.ExtractionRuns
            .Where(r => r.Id == id && (r.Status == ExtractionRunStatus.Queued || r.Status == ExtractionRunStatus.Running))
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, ExtractionRunStatus.Cancelled)
                .SetProperty(r => r.CompletedAt, now), ct);
        var run = await db.ExtractionRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
        return run is null ? new NotFoundResult() : new OkObjectResult(run.ToDto());
    }

    [Function("RunExtraction")]
    public Task Run([QueueTrigger(QueueNames.ExtractionRuns)] string runId, CancellationToken ct) =>
        Guid.TryParse(runId, out var id) ? runner.RunAsync(id, ct) : Task.CompletedTask;
}
