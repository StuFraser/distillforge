using DistillForge.Core.Contracts;
using DistillForge.Core.Extraction;
using DistillForge.Data;
using DistillForge.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DistillForge.Api.Services;

/// <summary>
/// Single-window pipeline: extract → validate (segment IDs, quotes) → resolve offsets → persist.
/// Multi-window, dedupe and consolidation slot in between extract and validate later.
/// </summary>
public sealed class ExtractionRunner(DistillForgeDbContext db, IActionExtractor extractor, ILogger<ExtractionRunner> logger)
{
    public async Task RunAsync(Guid runId, CancellationToken ct)
    {
        var run = await db.ExtractionRuns.FirstOrDefaultAsync(r => r.Id == runId, ct);
        if (run is null)
        {
            logger.LogWarning("Extraction run {RunId} not found (workspace replaced?); skipping.", runId);
            return;
        }
        if (run.Status != ExtractionRunStatus.Queued)
        {
            logger.LogWarning("Extraction run {RunId} is {Status}, not Queued; skipping redelivery.", runId, run.Status);
            return;
        }

        run.Status = ExtractionRunStatus.Running;
        run.StartedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        try
        {
            var workspace = await db.Workspaces
                .Include(w => w.Segments)
                .FirstAsync(w => w.Id == run.WorkspaceId, ct);
            var segments = workspace.ToCoreSegments();
            run.EstimatedPromptTokens = Ai.ExtractionPrompt.EstimateTokens(segments);

            var result = await extractor.ExtractAsync(segments, ct);
            var outcome = ExtractionValidator.Validate(result.Response, segments);

            // Re-running extraction replaces previously generated notes; manual notes are kept.
            await db.ActionItems.Where(a => a.WorkspaceId == workspace.Id && !a.IsManual).ExecuteDeleteAsync(ct);

            var segmentIds = workspace.Segments.ToDictionary(s => s.Label, s => s.Id);
            var now = DateTimeOffset.UtcNow;
            int order = 0;
            foreach (var ap in outcome.ActionPoints)
            {
                db.ActionItems.Add(new ActionItem
                {
                    Id = Guid.CreateVersion7(),
                    WorkspaceId = workspace.Id,
                    ExtractionRunId = run.Id,
                    Title = Clip(ap.Title, 300)!,
                    Summary = ap.Summary,
                    Owner = Clip(ap.Owner, 200),
                    Due = Clip(ap.Due, 100),
                    SortOrder = order++,
                    CreatedAt = now,
                    Evidence = ap.Evidence.Select(e => new Evidence
                    {
                        Id = Guid.CreateVersion7(),
                        SegmentId = segmentIds[e.SegmentLabel],
                        Quote = e.Quote,
                        Start = e.Start,
                        End = e.End,
                        MatchQuality = e.Quality,
                    }).ToList(),
                });
            }

            run.Model = result.Model;
            run.FinishReason = result.FinishReason;
            run.PromptTokens = result.Usage?.PromptTokens;
            run.CompletionTokens = result.Usage?.CompletionTokens;
            run.ActionItemCount = outcome.ActionPoints.Count;
            var warnings = result.Warnings.Concat(outcome.Warnings).ToList();
            run.Warnings = warnings.Count > 0 ? string.Join('\n', warnings) : null;
            run.Status = ExtractionRunStatus.Succeeded;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // Don't rethrow: a queue retry would repeat an expensive (and likely identical) model call.
            logger.LogError(ex, "Extraction run {RunId} failed.", runId);
            db.ChangeTracker.Clear();
            db.Attach(run);
            run.Status = ExtractionRunStatus.Failed;
            run.Error = ex is Ai.ExtractionFailedException or HttpRequestException or TaskCanceledException
                ? ex.Message
                : $"{ex.GetType().Name}: {ex.Message}";
        }

        run.CompletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(CancellationToken.None);
    }

    private static string? Clip(string? s, int max) => s is null || s.Length <= max ? s : s[..max];
}
