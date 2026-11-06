using DistillForge.Core.Extraction;

namespace DistillForge.Core.Tests;

public class ActionPointDeduplicatorTests
{
    private static ValidatedActionPoint Ap(string title, string segment, int start, string? owner = null, string? due = null, string? summary = null) =>
        new(title, summary, owner, due, [new ResolvedEvidence(segment, "q", start, start + 5, MatchQuality.Exact)]);

    [Fact]
    public void MergesSameItemSeenByTwoWindows()
    {
        var a = Ap("Send revised budget to finance", "S10", 100, owner: "Sam");
        var b = Ap("Send the revised budget to finance", "S10", 100, due: "Friday", summary: "Needed before the review.");

        var merged = ActionPointDeduplicator.Merge([a, b]);

        var m = Assert.Single(merged);
        Assert.Equal("Sam", m.Owner);        // filled from a
        Assert.Equal("Friday", m.Due);       // filled from b
        Assert.Equal("Needed before the review.", m.Summary);
    }

    [Fact]
    public void MergesAcrossAdjacentSegmentsAndUnionsEvidence()
    {
        var a = Ap("Book the meeting room", "S10", 100);
        var b = Ap("Book meeting room", "S11", 160);

        var m = Assert.Single(ActionPointDeduplicator.Merge([a, b]));
        Assert.Equal(2, m.Evidence.Count);
        Assert.True(m.Evidence[0].Start < m.Evidence[1].Start);
    }

    [Fact]
    public void KeepsSimilarTitlesFarApart()
    {
        // Same wording but different places in the document: two genuine tasks.
        var a = Ap("Send status update to Bob", "S5", 50);
        var b = Ap("Send status update to Bob", "S80", 800);
        Assert.Equal(2, ActionPointDeduplicator.Merge([a, b]).Count);
    }

    [Fact]
    public void KeepsDifferentItemsInTheSameSegment()
    {
        var a = Ap("Send revised budget to finance", "S10", 100);
        var b = Ap("Book the meeting room", "S10", 110);
        Assert.Equal(2, ActionPointDeduplicator.Merge([a, b]).Count);
    }

    [Fact]
    public void SingleSharedWordIsNotEnough()
    {
        Assert.False(ActionPointDeduplicator.TitlesSimilar("Review budget", "Approve budget changes"));
    }

    [Fact]
    public void PluralsAndTensesFold()
    {
        Assert.True(ActionPointDeduplicator.TitlesSimilar("Book rooms for workshops", "Booking room for workshop"));
    }
}
