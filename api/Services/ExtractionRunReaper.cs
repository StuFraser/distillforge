using DistillForge.Core.Contracts;
using DistillForge.Core.Extraction;
using DistillForge.Data;
using DistillForge.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace DistillForge.Api.Services;

/// <summary>
/// Fails a run whose worker disappeared (host killed, crashed or lost power) so it doesn't stay Running forever.
/// Called lazily from the endpoints that look at a run; there is no background sweeper.
/// </summary>
public static class ExtractionRunReaper
{
    /// <summary>
    /// If <paramref name="run"/> (already loaded, untracked) is stale, marks it Failed and returns true.
    /// The check happens in memory first, so a healthy poll costs no write. The UPDATE re-checks the heartbeat
    /// in its WHERE clause, so it can't clobber a run whose worker just stamped it, or one that finished meanwhile.
    /// </summary>
    public static async Task<bool> FailIfStaleAsync(DistillForgeDbContext db, ExtractionRun run, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        if (!RunStaleness.IsStale(run.Status, run.StartedAt, run.HeartbeatAt, now)) return false;

        var cutoff = now - RunStaleness.StaleAfter;
        var failed = await db.ExtractionRuns
            .Where(r => r.Id == run.Id && r.Status == ExtractionRunStatus.Running
                && (r.HeartbeatAt ?? r.StartedAt) < cutoff)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, ExtractionRunStatus.Failed)
                .SetProperty(r => r.Error, RunStaleness.InterruptedMessage)
                .SetProperty(r => r.CompletedAt, now), ct);

        // Reflect the change on the caller's copy whether or not we won the race.
        if (failed > 0)
        {
            run.Status = ExtractionRunStatus.Failed;
            run.Error = RunStaleness.InterruptedMessage;
            run.CompletedAt = now;
        }
        return failed > 0;
    }
}
