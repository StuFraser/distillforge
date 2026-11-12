using DistillForge.Core.Contracts;
using DistillForge.Core.Extraction;

namespace DistillForge.Core.Tests;

public class RunStalenessTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Fresh_heartbeat_is_not_stale() =>
        Assert.False(RunStaleness.IsStale(ExtractionRunStatus.Running, Now.AddMinutes(-30), Now.AddSeconds(-10), Now));

    [Fact]
    public void Quiet_heartbeat_is_stale() =>
        Assert.True(RunStaleness.IsStale(ExtractionRunStatus.Running, Now.AddMinutes(-30), Now.AddSeconds(-91), Now));

    [Fact]
    public void Exactly_at_the_threshold_is_not_stale_yet() =>
        Assert.False(RunStaleness.IsStale(ExtractionRunStatus.Running, Now.AddMinutes(-30), Now - RunStaleness.StaleAfter, Now));

    [Fact]
    public void Missing_heartbeat_falls_back_to_start_time()
    {
        Assert.True(RunStaleness.IsStale(ExtractionRunStatus.Running, Now.AddMinutes(-5), null, Now));
        Assert.False(RunStaleness.IsStale(ExtractionRunStatus.Running, Now.AddSeconds(-20), null, Now));
    }

    [Fact]
    public void Running_with_no_timestamps_is_treated_as_orphaned() =>
        Assert.True(RunStaleness.IsStale(ExtractionRunStatus.Running, null, null, Now));

    [Theory]
    [InlineData(ExtractionRunStatus.Queued)]
    [InlineData(ExtractionRunStatus.Succeeded)]
    [InlineData(ExtractionRunStatus.Failed)]
    [InlineData(ExtractionRunStatus.Cancelled)]
    public void Only_running_runs_can_be_stale(ExtractionRunStatus status) =>
        Assert.False(RunStaleness.IsStale(status, Now.AddHours(-3), Now.AddHours(-3), Now));

    [Fact]
    public void Custom_threshold_is_honoured() =>
        Assert.True(RunStaleness.IsStale(ExtractionRunStatus.Running, Now.AddMinutes(-1), Now.AddSeconds(-20), Now, TimeSpan.FromSeconds(15)));
}
