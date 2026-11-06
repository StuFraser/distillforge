using DistillForge.Core.Extraction;
using DistillForge.Core.Segmentation;

namespace DistillForge.Core.Tests;

public class WindowPlannerTests
{
    // Each segment costs tokens + PerSegmentOverhead (8) = 20 with tokens = 12.
    private static List<Segment> Make(int n, int tokens = 12) =>
        Enumerable.Range(1, n).Select(i => new Segment($"S{i}", i, i * 10, i * 10 + 9, "x", tokens)).ToList();

    [Fact]
    public void SmallDocumentIsOneWindow()
    {
        var windows = WindowPlanner.Plan(Make(5), maxWindowTokens: 1000);
        var w = Assert.Single(windows);
        Assert.Equal(1, w.Index);
        Assert.Equal(1, w.Count);
        Assert.Equal(5, w.Segments.Count);
    }

    [Fact]
    public void EmptyDocumentHasNoWindows() => Assert.Empty(WindowPlanner.Plan([], 100));

    [Fact]
    public void WindowsFitBudgetCoverEverythingAndOverlap()
    {
        var segments = Make(100);
        const int budget = 200; // 10 segments per window, overlap = 12% = 24 tokens -> 1 segment

        var windows = WindowPlanner.Plan(segments, budget);

        Assert.True(windows.Count > 1);
        Assert.All(windows, w => Assert.True(w.Segments.Sum(s => s.TokenEstimate + 8) <= budget));
        Assert.All(windows, (w, i) => { Assert.Equal(i + 1, w.Index); Assert.Equal(windows.Count, w.Count); });

        // Every segment appears in some window.
        var covered = windows.SelectMany(w => w.Segments.Select(s => s.Label)).ToHashSet();
        Assert.Equal(100, covered.Count);

        // Consecutive windows share at least one segment and always move forward.
        for (int i = 1; i < windows.Count; i++)
        {
            var prev = windows[i - 1].Segments;
            var cur = windows[i].Segments;
            Assert.Contains(cur[0], prev);
            Assert.True(cur[0].Ordinal > prev[0].Ordinal);
        }
        Assert.Equal("S100", windows[^1].Segments[^1].Label);
    }

    [Fact]
    public void ZeroOverlapProducesDisjointWindows()
    {
        var windows = WindowPlanner.Plan(Make(30), 200, overlapFraction: 0);
        var labels = windows.SelectMany(w => w.Segments.Select(s => s.Label)).ToList();
        Assert.Equal(labels.Distinct().Count(), labels.Count);
    }

    [Fact]
    public void OversizedSegmentStillTerminates()
    {
        var segments = Make(3, tokens: 500);
        var windows = WindowPlanner.Plan(segments, maxWindowTokens: 100);
        Assert.Equal(3, windows.Count);
        Assert.All(windows, w => Assert.Single(w.Segments));
    }
}
