using System.Text.Json;
using DistillForge.Core.Extraction;
using DistillForge.Core.Segmentation;

namespace DistillForge.Core.Tests;

public class ExtractionValidatorTests
{
    private const string Source = "Sam: I'll get the revised budget over by Friday.\nAlex: I'll book the room.\nPriya: Nice weather.";
    private static readonly IReadOnlyList<Segment> Segments = new Segmenter().Split(Source, SegmentationStrategyKind.Transcript);

    private static ExtractionResponse Parse(string json) => JsonSerializer.Deserialize<ExtractionResponse>(json)!;

    [Fact]
    public void ResolvesValidEvidence()
    {
        var response = Parse("""
            {"action_points":[{"id":"ap_1","title":"Send revised budget","summary":"To finance.","owner":"Sam","due":"Friday",
              "evidence":[{"segment_id":"S1","quote":"I'll get the revised budget over by Friday"}]}]}
            """);

        var outcome = ExtractionValidator.Validate(response, Segments);

        var ap = Assert.Single(outcome.ActionPoints);
        Assert.Equal("Sam", ap.Owner);
        var ev = Assert.Single(ap.Evidence);
        Assert.Equal(MatchQuality.Exact, ev.Quality);
        Assert.Equal("I'll get the revised budget over by Friday", Source[ev.Start..ev.End]);
        Assert.Empty(outcome.Warnings);
    }

    [Fact]
    public void DropsActionPointsWithOnlyHallucinatedSegmentIds()
    {
        var response = Parse("""
            {"action_points":[
              {"id":"ap_1","title":"Invented task","summary":"","owner":null,"due":null,"evidence":[{"segment_id":"S99","quote":"x"}]},
              {"id":"ap_2","title":"","summary":"","owner":null,"due":null,"evidence":[{"segment_id":"S1","quote":"x"}]},
              {"id":"ap_3","title":"Book the room","summary":"","owner":"null","due":"","evidence":[{"segment_id":"[s2]","quote":"Book the  room"},{"segment_id":"S42","quote":"y"}]}
            ]}
            """);

        var outcome = ExtractionValidator.Validate(response, Segments);

        var ap = Assert.Single(outcome.ActionPoints);
        Assert.Equal("Book the room", ap.Title);
        Assert.Null(ap.Owner);
        Assert.Null(ap.Due);
        Assert.Equal(MatchQuality.Normalised, Assert.Single(ap.Evidence).Quality);
        Assert.Contains(outcome.Warnings, w => w.Contains("S99"));
        Assert.Contains(outcome.Warnings, w => w.Contains("S42"));
    }

    [Fact]
    public void UnresolvedQuoteKeepsItemWithSegmentHighlight()
    {
        var response = Parse("""
            {"action_points":[{"id":"ap_1","title":"Book room","summary":"","owner":null,"due":null,
              "evidence":[{"segment_id":"S2","quote":"reserve the boardroom"}]}]}
            """);

        var ap = Assert.Single(ExtractionValidator.Validate(response, Segments).ActionPoints);
        Assert.Equal(MatchQuality.Segment, Assert.Single(ap.Evidence).Quality);
    }

    [Theory]
    [InlineData("S12", "S12")]
    [InlineData("s12", "S12")]
    [InlineData("[S12]", "S12")]
    [InlineData(" 12 ", "S12")]
    [InlineData("segment 12", null)]
    [InlineData("S", null)]
    [InlineData(null, null)]
    public void NormalisesSegmentIds(string? input, string? expected)
    {
        Assert.Equal(expected, ExtractionValidator.NormaliseSegmentId(input));
    }
}
