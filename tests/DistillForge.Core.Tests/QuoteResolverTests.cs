using DistillForge.Core.Extraction;
using DistillForge.Core.Segmentation;

namespace DistillForge.Core.Tests;

public class QuoteResolverTests
{
    private const string Source = "Intro line.\n\nSam: Yeah, I’ll get the revised   budget over by Friday — promise.";

    private static Segment Seg()
    {
        int start = Source.IndexOf("Sam", StringComparison.Ordinal);
        return new Segment("S2", 2, start, Source.Length, Source[start..], 10);
    }

    [Fact]
    public void Exact()
    {
        var r = QuoteResolver.Resolve(Seg(), "the revised   budget");
        Assert.Equal(MatchQuality.Exact, r.Quality);
        Assert.Equal("the revised   budget", Source[r.Start..r.End]);
    }

    [Fact]
    public void Normalised_WhitespaceCurlyQuotesDashesCase()
    {
        var r = QuoteResolver.Resolve(Seg(), "\"I'll get the Revised budget over by Friday - promise\"");
        Assert.Equal(MatchQuality.Normalised, r.Quality);
        Assert.Equal("I’ll get the revised   budget over by Friday — promise", Source[r.Start..r.End]);
    }

    [Fact]
    public void Normalised_AcrossEllipsis()
    {
        var r = QuoteResolver.Resolve(Seg(), "I'll get ... by Friday");
        Assert.Equal(MatchQuality.Normalised, r.Quality);
        Assert.Equal("I’ll get the revised   budget over by Friday", Source[r.Start..r.End]);
    }

    [Theory]
    [InlineData("send the contract to legal")]
    [InlineData("")]
    [InlineData(null)]
    public void FallsBackToWholeSegment(string? quote)
    {
        var seg = Seg();
        var r = QuoteResolver.Resolve(seg, quote);
        Assert.Equal(MatchQuality.Segment, r.Quality);
        Assert.Equal((seg.Start, seg.End), (r.Start, r.End));
    }
}
