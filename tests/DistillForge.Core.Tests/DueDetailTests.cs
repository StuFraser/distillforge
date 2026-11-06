using System.Text.Json;
using DistillForge.Core.Extraction;
using DistillForge.Core.Segmentation;

namespace DistillForge.Core.Tests;

public class DueDetailTests
{
    private const string Source = "Sam: Ideally the week of the 16th of November we should have the report out.";
    private static readonly IReadOnlyList<Segment> Segments = new Segmenter().Split(Source, SegmentationStrategyKind.Transcript);

    private static ValidatedActionPoint Validate(string due, string? dueDetail, string? summary = "Report goes to the board.")
    {
        var response = JsonSerializer.Deserialize<ExtractionResponse>(JsonSerializer.Serialize(new
        {
            action_points = new[]
            {
                new
                {
                    id = "ap_1", title = "Publish report", summary, owner = "Sam", due, due_detail = dueDetail,
                    evidence = new[] { new { segment_id = "S1", quote = "we should have the report out" } },
                },
            },
        }))!;
        return Assert.Single(ExtractionValidator.Validate(response, Segments).ActionPoints);
    }

    [Fact]
    public void KeepsShortDueAndMovesFullWordingIntoSummary()
    {
        var ap = Validate("16 Nov", "ideally the week of the 16th of November");

        Assert.Equal("16 Nov", ap.Due);
        Assert.Equal("Report goes to the board. Deadline as stated: “ideally the week of the 16th of November”.", ap.Summary);
    }

    [Fact]
    public void NoDuplicationWhenDetailJustRepeatsTheDueTag()
    {
        var ap = Validate("Friday", "friday.");
        Assert.Equal("Report goes to the board.", ap.Summary);
    }

    [Fact]
    public void DetailBecomesTheSummaryWhenThereIsNoSummary()
    {
        var ap = Validate("16 Nov", "the week of the 16th of November", summary: null);
        Assert.Equal("Deadline as stated: “the week of the 16th of November”.", ap.Summary);
    }

    [Fact]
    public void ResponsesWithoutDueDetailStillParse()
    {
        var response = JsonSerializer.Deserialize<ExtractionResponse>(
            """{"action_points":[{"id":"ap_1","title":"T","summary":"S","owner":null,"due":"Friday","evidence":[]}]}""")!;
        Assert.Null(response.ActionPoints[0].DueDetail);
    }
}
