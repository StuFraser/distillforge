using System.Text.Json;
using DistillForge.Core.Extraction;

namespace DistillForge.Core.Tests;

public class PossibleDuplicatesTests
{
    private static ValidatedActionPoint Ap(string title, int seg) =>
        new(title, null, null, null, [new ResolvedEvidence($"S{seg}", "q", seg * 10, seg * 10 + 5, MatchQuality.Exact)]);

    [Fact]
    public void SimilarTitlesFarApartArePaired()
    {
        // The request early on and the confirmation at the close: too far apart for the nearby-merge, so they get flagged.
        IReadOnlyList<ValidatedActionPoint> points =
        [
            Ap("Send journal template and CSV", 5),
            Ap("Book meeting room", 40),
            Ap("Confirm journal template and CSV requirements", 90),
        ];
        Assert.Empty(ActionPointDeduplicator.Merge(points).Skip(3)); // merge leaves all three
        Assert.Equal(3, ActionPointDeduplicator.Merge(points).Count);
        Assert.Equal([(0, 2)], ActionPointDeduplicator.SimilarPairs(points));
    }

    [Fact]
    public void UnrelatedTitlesAreNotPaired() =>
        Assert.Empty(ActionPointDeduplicator.SimilarPairs([Ap("Book room", 1), Ap("Send budget to finance", 50)]));

    private static readonly IReadOnlyList<ValidatedActionPoint> Points =
        [Ap("One", 1), Ap("Two", 20), Ap("Three", 40), Ap("Four", 60)];

    private static ConsolidationResponse Parse(string json) => JsonSerializer.Deserialize<ConsolidationResponse>(json)!;

    [Fact]
    public void ResponseWithoutPossibleStillParses() =>
        Assert.Empty(Parse("""{"merges":[]}""").Possible);

    [Fact]
    public void FinalIndexFollowsMergesThenPossiblePairsUseIt()
    {
        // c2 is folded into c1, so the list shrinks to [One+Two, Three, Four]. A possible pair (c2,c4) becomes (0,2).
        var response = Parse("""{"merges":[{"keep":"c1","duplicates":["c2"]}],"possible":[{"ids":["c2","c4"]}]}""");
        var outcome = ActionPointConsolidator.Apply(Points, response);
        Assert.Equal(3, outcome.ActionPoints.Count);
        Assert.Equal([0, 0, 1, 2], outcome.FinalIndexOf);
        Assert.Equal([(0, 2)], ActionPointConsolidator.PossiblePairs(response, outcome.FinalIndexOf));
    }

    [Fact]
    public void PossiblePairsDropUnknownIdsAndCollapsedPairs()
    {
        var response = Parse("""{"merges":[{"keep":"c1","duplicates":["c2"]}],"possible":[{"ids":["c1","c2"]},{"ids":["c3","zzz"]},{"ids":["c3","c4","c3"]}]}""");
        var outcome = ActionPointConsolidator.Apply(Points, response);
        Assert.Equal([(1, 2)], ActionPointConsolidator.PossiblePairs(response, outcome.FinalIndexOf));
    }
}
