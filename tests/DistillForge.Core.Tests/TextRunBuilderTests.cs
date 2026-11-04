using DistillForge.Core.Extraction;
using DistillForge.Core.Highlighting;

namespace DistillForge.Core.Tests;

public class TextRunBuilderTests
{
    [Fact]
    public void NoHighlights_SingleRun()
    {
        var runs = TextRunBuilder.Build(10, []);
        Assert.Equal([new TextRun(0, 10, null)], runs);
    }

    [Fact]
    public void SplitsAroundRanges_AndBestQualityWinsOnOverlap()
    {
        var runs = TextRunBuilder.Build(20,
        [
            new HighlightRange(2, 10, MatchQuality.Segment),
            new HighlightRange(5, 8, MatchQuality.Exact),
            new HighlightRange(15, 30, MatchQuality.Normalised), // clamped
        ]);

        Assert.Equal(
        [
            new TextRun(0, 2, null),
            new TextRun(2, 5, MatchQuality.Segment),
            new TextRun(5, 8, MatchQuality.Exact),
            new TextRun(8, 10, MatchQuality.Segment),
            new TextRun(10, 15, null),
            new TextRun(15, 20, MatchQuality.Normalised),
        ], runs);
    }

    [Fact]
    public void AdjacentSameQualityRangesMerge()
    {
        var runs = TextRunBuilder.Build(10, [new HighlightRange(0, 4, MatchQuality.Exact), new HighlightRange(4, 7, MatchQuality.Exact)]);
        Assert.Equal([new TextRun(0, 7, MatchQuality.Exact), new TextRun(7, 10, null)], runs);
    }
}
