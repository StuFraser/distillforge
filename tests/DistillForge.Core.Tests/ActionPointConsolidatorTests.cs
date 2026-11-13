using System.Text.Json;
using DistillForge.Core.Contracts;
using DistillForge.Core.Extraction;

namespace DistillForge.Core.Tests;

public class ActionPointConsolidatorTests
{
    private static ValidatedActionPoint Ap(string title, int start, string? owner = null, string? due = null) =>
        new(title, null, owner, due, [new ResolvedEvidence($"S{start}", "q", start * 10, start * 10 + 5, MatchQuality.Exact)]);

    private static readonly IReadOnlyList<ValidatedActionPoint> Points =
        [Ap("Send budget", 1, owner: "Sam"), Ap("Email finance the numbers", 40, due: "Friday"), Ap("Book room", 80)];

    private static ConsolidationResponse Parse(string json) => JsonSerializer.Deserialize<ConsolidationResponse>(json)!;

    [Fact]
    public void CandidatesGetStableIdsInOrder()
    {
        var c = ActionPointConsolidator.ToCandidates(Points);
        Assert.Equal(["c1", "c2", "c3"], c.Select(x => x.Id));
        Assert.Equal(["S40"], c[1].SegmentLabels);
    }

    [Fact]
    public void MergesGroupAndKeepsAllEvidenceAndFields()
    {
        var outcome = ActionPointConsolidator.Apply(Points, Parse("""{"merges":[{"keep":"c1","duplicates":["c2"]}]}"""));

        Assert.Equal(1, outcome.MergedCount);
        Assert.Equal(2, outcome.ActionPoints.Count);
        var merged = outcome.ActionPoints[0];
        Assert.Equal("Sam", merged.Owner);
        Assert.Equal("Friday", merged.Due);
        Assert.Equal(2, merged.Evidence.Count);
        Assert.Equal("Book room", outcome.ActionPoints[1].Title);
    }

    [Fact]
    public void IgnoresUnknownIdsSelfMergesAndChains()
    {
        var outcome = ActionPointConsolidator.Apply(Points, Parse("""
            {"merges":[
              {"keep":"c99","duplicates":["c1"]},
              {"keep":"c1","duplicates":["c1","c42"]},
              {"keep":"c1","duplicates":["c2"]},
              {"keep":"c2","duplicates":["c3"]},
              {"keep":"c3","duplicates":["c1"]}
            ]}
            """));

        // c2 was absorbed into c1, so later groups that keep c2, or fold in c1 (a keeper), must not apply.
        Assert.Equal(1, outcome.MergedCount);
        Assert.Equal(["Send budget", "Book room"], outcome.ActionPoints.Select(a => a.Title));
        Assert.NotEmpty(outcome.Warnings);
    }

    [Fact]
    public void EmptyResponseChangesNothing()
    {
        var outcome = ActionPointConsolidator.Apply(Points, new ConsolidationResponse());
        Assert.Equal(0, outcome.MergedCount);
        Assert.Equal(3, outcome.ActionPoints.Count);
    }

    [Fact]
    public void LowLevelTurnsEveryMergeIntoAPossibleGroup()
    {
        var response = Parse("""{"merges":[{"keep":"c1","duplicates":["c2","c3"]}],"possible":[{"ids":["c2","c3"]}]}""");
        var low = ActionPointConsolidator.ForLevel(response, MergeLevel.Low);

        Assert.Empty(low.Merges);
        Assert.Equal(2, low.Possible.Count);
        Assert.Equal(["c1", "c2", "c3"], low.Possible[1].Ids);
        Assert.Same(response, ActionPointConsolidator.ForLevel(response, MergeLevel.Medium));
        Assert.Same(response, ActionPointConsolidator.ForLevel(response, MergeLevel.Aggressive));
    }

    [Theory]
    [InlineData(null, "Grace", true)]
    [InlineData("Grace", "", true)]
    [InlineData("Grace", "Grace Okafor", true)]
    [InlineData("grace okafor", "GRACE", true)]
    [InlineData("Grace", "Dan", false)]
    public void OwnerCompatibility(string? a, string? b, bool expected) =>
        Assert.Equal(expected, ActionPointConsolidator.OwnersCompatible(a, b));

    [Fact]
    public void OwnerGuardRefusesMergeAndReportsItBlocked()
    {
        IReadOnlyList<ValidatedActionPoint> pts = [Ap("Send list", 1, owner: "Grace"), Ap("Send the list", 2, owner: "Dan"), Ap("Send list again", 3, owner: "Grace Okafor")];
        var response = Parse("""{"merges":[{"keep":"c1","duplicates":["c2","c3"]}]}""");

        var guarded = ActionPointConsolidator.Apply(pts, response, guardOwners: true);
        Assert.Equal(1, guarded.MergedCount);          // c3 (same person) merged, c2 (Dan) not
        Assert.Equal(2, guarded.ActionPoints.Count);
        Assert.Equal([(0, 1)], guarded.Blocked);

        var open = ActionPointConsolidator.Apply(pts, response);
        Assert.Equal(2, open.MergedCount);
        Assert.Single(open.ActionPoints);
    }
}
