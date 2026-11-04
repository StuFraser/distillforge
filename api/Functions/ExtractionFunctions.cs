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

    [Function("RunExtraction")]
    public Task Run([QueueTrigger(QueueNames.ExtractionRuns)] string runId, CancellationToken ct) =>
        Guid.TryParse(runId, out var id) ? runner.RunAsync(id, ct) : Task.CompletedTask;
}
