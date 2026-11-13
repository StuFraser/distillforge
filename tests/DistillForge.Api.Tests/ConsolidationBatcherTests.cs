using DistillForge.Api.Ai;
using DistillForge.Api.Services;
using DistillForge.Core.Contracts;
using DistillForge.Core.Extraction;
using DistillForge.Core.Segmentation;
using Microsoft.Extensions.Logging.Abstractions;

namespace DistillForge.Api.Tests;

public class ConsolidationBatcherTests
{
    private static ValidatedActionPoint Ap(string title, int pos) =>
        new(title, null, null, null, [new ResolvedEvidence($"S{pos}", "q", pos * 10, pos * 10 + 5, MatchQuality.Exact)]);

    private static ValidatedActionPoint Owned(string title, int pos, string owner) => Ap(title, pos) with { Owner = owner };

    /// <summary>Costs 20 tokens of prompt per candidate plus 10 overhead; merges candidates with identical titles.</summary>
    private sealed class FakeExtractor(int budget) : IActionExtractor
    {
        public int PromptBudgetTokens => budget;
        public List<List<string>> Batches { get; } = [];
        public Func<int, bool> FailCall { get; set; } = _ => false;
        public List<string[]> PossibleTitles { get; } = [];

        public int EstimateConsolidationTokens(IReadOnlyList<ConsolidationCandidate> c) => 10 + 20 * c.Count;

        public Task<ExtractionResult> ExtractAsync(ExtractionWindow window, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<ConsolidationResult> ConsolidateAsync(IReadOnlyList<ConsolidationCandidate> candidates, MergeLevel level, CancellationToken ct = default)
        {
            Batches.Add(candidates.Select(c => c.Title).ToList());
            if (FailCall(Batches.Count)) throw new ExtractionFailedException("boom");

            var response = new ConsolidationResponse();
            foreach (var g in candidates.GroupBy(c => c.Title).Where(g => g.Count() > 1))
                response.Merges.Add(new MergeGroup { Keep = g.First().Id, Duplicates = g.Skip(1).Select(c => c.Id).ToList() });
            foreach (var titles in PossibleTitles)
            {
                var ids = candidates.Where(c => titles.Contains(c.Title)).Select(c => c.Id).ToList();
                if (ids.Count == titles.Length) response.Possible.Add(new PossibleGroup { Ids = ids });
            }
            return Task.FromResult(new ConsolidationResult(response, "fake", "stop", new TokenUsage(100, 10, 110), []));
        }
    }

    private static Task<BatchedConsolidation> Run(IReadOnlyList<ValidatedActionPoint> pts, FakeExtractor x, MergeLevel level = MergeLevel.Medium) =>
        ConsolidationBatcher.RunAsync(pts, x, NullLogger.Instance, CancellationToken.None, level);

    [Fact]
    public async Task ListThatFitsIsOneCallAsBefore()
    {
        var x = new FakeExtractor(budget: 1000);
        var r = await Run([Ap("a", 1), Ap("b", 2), Ap("a", 3)], x);

        Assert.Single(x.Batches);
        Assert.Equal(1, r.Calls);
        Assert.Equal(1, r.MergedCount);
        Assert.Equal(["a", "b"], r.Points.Select(p => p.Title));
        Assert.Equal(100, r.PromptTokens);
    }

    [Fact]
    public async Task OversizedListIsSplitSoEveryCallFitsTheBudget()
    {
        var x = new FakeExtractor(budget: 110); // 5 candidates per call
        var pts = Enumerable.Range(1, 20).Select(i => Ap($"task {i}", i)).ToList();
        var r = await Run(pts, x);

        Assert.All(x.Batches, b => Assert.True(10 + 20 * b.Count <= 110));
        Assert.True(r.Calls > 1);
        Assert.Equal(20, r.Points.Count); // nothing lost, nothing invented
        Assert.Equal(0, r.MergedCount);
    }

    [Fact]
    public async Task DuplicatesAcrossABatchBoundaryAreCaughtBySecondRound()
    {
        var x = new FakeExtractor(budget: 110); // 5 per call: batches [1-5][6-10]
        var pts = new List<ValidatedActionPoint>
        {
            Ap("t1", 1), Ap("t2", 2), Ap("t3", 3), Ap("t4", 4), Ap("dup", 5),
            Ap("dup", 6), Ap("t7", 7), Ap("t8", 8), Ap("t9", 9), Ap("t10", 10),
        };
        var r = await Run(pts, x);

        Assert.Equal(1, r.MergedCount);
        Assert.Equal(9, r.Points.Count);
        Assert.Single(r.Points, p => p.Title == "dup");
        Assert.Equal(2, r.Points.Single(p => p.Title == "dup").Evidence.Count);
    }

    [Fact]
    public async Task OneFailedBatchKeepsItsPointsAndOthersStillConsolidate()
    {
        var x = new FakeExtractor(budget: 110) { FailCall = n => n == 1 };
        var pts = new List<ValidatedActionPoint>
        {
            Ap("x", 1), Ap("x", 2), Ap("t3", 3), Ap("t4", 4), Ap("t5", 5),
            Ap("y", 6), Ap("y", 7), Ap("t8", 8), Ap("t9", 9), Ap("t10", 10),
        };
        var r = await Run(pts, x);

        Assert.True(r.FailedCalls >= 1);
        Assert.Contains(r.Warnings, w => w.Contains("batch failed"));
        Assert.Single(r.Points, p => p.Title == "y");            // second batch merged
        Assert.True(r.Points.Count(p => p.Title == "x") >= 1);   // first batch's points survive
    }

    [Fact]
    public async Task BudgetTooSmallForTwoCandidatesIsSkippedWithoutCalls()
    {
        var x = new FakeExtractor(budget: 40); // one candidate = 30, two = 50
        var r = await Run([Ap("a", 1), Ap("a", 2)], x);

        Assert.True(r.Skipped);
        Assert.Empty(x.Batches);
        Assert.Equal(2, r.Points.Count);
        Assert.Contains(r.Warnings, w => w.Contains("skipped"));
    }

    [Fact]
    public async Task CapsCandidatesPerCallEvenWhenBudgetAllowsMore()
    {
        var x = new FakeExtractor(budget: 100_000);
        var pts = Enumerable.Range(1, 100).Select(i => Ap($"task {i}", i)).ToList();
        await Run(pts, x);

        Assert.All(x.Batches, b => Assert.True(b.Count <= ConsolidationBatcher.MaxCandidatesPerBatch));
    }

    [Fact]
    public async Task PossibleDuplicatePairsPointAtTheFinalList()
    {
        var x = new FakeExtractor(budget: 100_000);
        x.PossibleTitles.Add(["p", "q"]);
        var r = await Run([Ap("dup", 1), Ap("dup", 2), Ap("p", 3), Ap("mid", 4), Ap("q", 5)], x);

        var pair = Assert.Single(r.PossiblePairs);
        Assert.Equal("p", r.Points[pair.A].Title);
        Assert.Equal("q", r.Points[pair.B].Title);
    }

    [Fact]
    public async Task PossiblePairsSurviveTheSecondRoundRemap()
    {
        var x = new FakeExtractor(budget: 110);
        x.PossibleTitles.Add(["p", "q"]);
        var pts = new List<ValidatedActionPoint>
        {
            Ap("d", 1), Ap("d", 2), Ap("p", 3), Ap("q", 4), Ap("t5", 5),
            Ap("t6", 6), Ap("t7", 7), Ap("t8", 8), Ap("t9", 9), Ap("t10", 10),
        };
        var r = await Run(pts, x);

        var pair = Assert.Single(r.PossiblePairs);
        Assert.Equal("p", r.Points[pair.A].Title);
        Assert.Equal("q", r.Points[pair.B].Title);
        Assert.Equal(9, r.Points.Count);
    }

    [Fact]
    public async Task LowNeverMergesButFlagsWhatTheModelWouldHaveMerged()
    {
        var x = new FakeExtractor(budget: 100_000);
        var r = await Run([Ap("a", 1), Ap("b", 2), Ap("a", 3)], x, MergeLevel.Low);

        Assert.Equal(0, r.MergedCount);
        Assert.Equal(3, r.Points.Count);
        var pair = Assert.Single(r.PossiblePairs);
        Assert.Equal(("a", "a"), (r.Points[pair.A].Title, r.Points[pair.B].Title));
    }

    [Fact]
    public async Task MediumFlagsInsteadOfMergingWhenOwnersDiffer()
    {
        var x = new FakeExtractor(budget: 100_000);
        var r = await Run([Owned("Send list", 1, "Grace"), Owned("Send list", 2, "Dan"), Owned("Send list", 3, "Grace Okafor")], x);

        Assert.Equal(1, r.MergedCount);
        Assert.Equal(2, r.Points.Count);
        Assert.Single(r.PossiblePairs);
    }

    [Fact]
    public async Task AggressiveMergesEvenWhenOwnersDiffer()
    {
        var x = new FakeExtractor(budget: 100_000);
        var r = await Run([Owned("Send list", 1, "Grace"), Owned("Send list", 2, "Dan")], x, MergeLevel.Aggressive);

        Assert.Equal(1, r.MergedCount);
        Assert.Single(r.Points);
        Assert.Empty(r.PossiblePairs);
    }
}
