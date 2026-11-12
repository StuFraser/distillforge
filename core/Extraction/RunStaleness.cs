using DistillForge.Core.Contracts;

namespace DistillForge.Core.Extraction;

/// <summary>
/// Detects extraction runs orphaned by a hard process kill. A worker stamps <c>HeartbeatAt</c> every
/// <see cref="HeartbeatInterval"/> while it executes a run; a <c>Running</c> run whose heartbeat has been
/// quiet for <see cref="StaleAfter"/> has no worker any more and can be failed. Only <c>Running</c> runs
/// qualify: a <c>Queued</c> run may legitimately wait behind other runs, and the user can cancel it.
/// </summary>
public static class RunStaleness
{
    public static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(10);

    /// <summary>Nine missed heartbeats. Independent of how slow a model call is, because the stamp comes from a timer, not from progress.</summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(90);

    public const string InterruptedMessage = "The extraction was interrupted (the API stopped responding). Run it again.";

    /// <param name="heartbeatAt">Null for runs that predate heartbeats; <paramref name="startedAt"/> is used instead.</param>
    public static bool IsStale(ExtractionRunStatus status, DateTimeOffset? startedAt, DateTimeOffset? heartbeatAt, DateTimeOffset now, TimeSpan? staleAfter = null)
    {
        if (status != ExtractionRunStatus.Running) return false;
        var last = heartbeatAt ?? startedAt;
        // Running with neither timestamp shouldn't happen (the claim sets both); treat as orphaned rather than stuck forever.
        if (last is null) return true;
        return now - last.Value > (staleAfter ?? StaleAfter);
    }
}
