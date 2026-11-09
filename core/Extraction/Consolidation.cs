using System.Text.Json.Serialization;

namespace DistillForge.Core.Extraction;

/// <summary>One candidate shown to the model in the consolidation pass. The model sees no more than this.</summary>
public sealed record ConsolidationCandidate(
    string Id, string Title, string? Summary, string? Owner, string? Due, IReadOnlyList<string> SegmentLabels);

/// <summary>
/// The model's answer: groups of candidates it judges to be the same task. It never rewrites
/// text or evidence; merging happens in code (<see cref="ActionPointConsolidator"/>).
/// </summary>
public sealed class ConsolidationResponse
{
    [JsonPropertyName("merges")]
    public List<MergeGroup> Merges { get; set; } = [];

    /// <summary>
    /// Looser groups: candidates that MIGHT be the same task (e.g. a request and its later confirmation).
    /// These are flagged for the user, never merged automatically.
    /// </summary>
    [JsonPropertyName("possible")]
    public List<PossibleGroup> Possible { get; set; } = [];
}

public sealed class PossibleGroup
{
    [JsonPropertyName("ids")]
    public List<string> Ids { get; set; } = [];
}

public sealed class MergeGroup
{
    [JsonPropertyName("keep")]
    public string? Keep { get; set; }

    [JsonPropertyName("duplicates")]
    public List<string> Duplicates { get; set; } = [];
}

public sealed record ConsolidationResult(
    ConsolidationResponse Response,
    string Model,
    string? FinishReason,
    TokenUsage? Usage,
    IReadOnlyList<string> Warnings);

/// <param name="FinalIndexOf">For each original candidate index, the index in <paramref name="ActionPoints"/> it ended up in (absorbed points map to their keeper).</param>
public sealed record ConsolidationOutcome(
    IReadOnlyList<ValidatedActionPoint> ActionPoints, int MergedCount, IReadOnlyList<string> Warnings, IReadOnlyList<int> FinalIndexOf);

/// <summary>Applies the model's merge groups defensively: bad ids, self-merges and chains are ignored.</summary>
public static class ActionPointConsolidator
{
    public static string IdFor(int index) => $"c{index + 1}";

    public static IReadOnlyList<ConsolidationCandidate> ToCandidates(IReadOnlyList<ValidatedActionPoint> points) =>
        points.Select((p, i) => new ConsolidationCandidate(
            IdFor(i), p.Title, p.Summary, p.Owner, p.Due,
            p.Evidence.Select(e => e.SegmentLabel).Distinct().ToList())).ToList();

    public static ConsolidationOutcome Apply(IReadOnlyList<ValidatedActionPoint> points, ConsolidationResponse response)
    {
        var byId = Enumerable.Range(0, points.Count).ToDictionary(IdFor, StringComparer.OrdinalIgnoreCase);
        var current = points.ToArray();
        var absorbed = new HashSet<int>();
        var keepers = new HashSet<int>();
        var warnings = new List<string>();

        foreach (var group in response.Merges)
        {
            if (group.Keep is null || !byId.TryGetValue(group.Keep.Trim(), out int keep))
            {
                warnings.Add($"Consolidation: ignored merge with unknown keep id '{group.Keep}'.");
                continue;
            }
            if (absorbed.Contains(keep)) continue;

            foreach (var dupId in group.Duplicates.Select(d => d.Trim()).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!byId.TryGetValue(dupId, out int dup))
                {
                    warnings.Add($"Consolidation: ignored unknown duplicate id '{dupId}'.");
                    continue;
                }
                if (dup == keep || absorbed.Contains(dup) || keepers.Contains(dup)) continue;

                current[keep] = ActionPointDeduplicator.Combine(current[keep], current[dup]);
                absorbed.Add(dup);
                keepers.Add(keep);
            }
        }

        var result = current.Where((_, i) => !absorbed.Contains(i)).ToList();

        // Original index -> index in `result`. Every absorbed point was folded into exactly one keeper.
        var newIndex = new int[points.Count];
        var finalIndexOf = new int[points.Count];
        int next = 0;
        for (int i = 0; i < points.Count; i++) newIndex[i] = absorbed.Contains(i) ? -1 : next++;
        foreach (var group in response.Merges)
        {
            if (group.Keep is null || !byId.TryGetValue(group.Keep.Trim(), out int keep)) continue;
            foreach (var dupId in group.Duplicates.Select(d => d.Trim()))
                if (byId.TryGetValue(dupId, out int dup) && absorbed.Contains(dup) && newIndex[keep] >= 0) finalIndexOf[dup] = newIndex[keep];
        }
        for (int i = 0; i < points.Count; i++) if (newIndex[i] >= 0) finalIndexOf[i] = newIndex[i];

        return new ConsolidationOutcome(result, absorbed.Count, warnings, finalIndexOf);
    }

    /// <summary>
    /// Turns the model's "possible duplicate" groups into pairs of indexes into the post-merge list
    /// (<see cref="ConsolidationOutcome.FinalIndexOf"/>). Unknown ids and pairs that collapsed into one point are dropped.
    /// </summary>
    public static IReadOnlyList<(int A, int B)> PossiblePairs(ConsolidationResponse response, IReadOnlyList<int> finalIndexOf)
    {
        var byId = Enumerable.Range(0, finalIndexOf.Count).ToDictionary(IdFor, StringComparer.OrdinalIgnoreCase);
        var pairs = new HashSet<(int, int)>();
        foreach (var group in response.Possible)
        {
            var members = group.Ids
                .Select(id => byId.TryGetValue(id.Trim(), out int i) ? finalIndexOf[i] : -1)
                .Where(i => i >= 0).Distinct().Order().ToList();
            for (int x = 0; x < members.Count; x++)
                for (int y = x + 1; y < members.Count; y++) pairs.Add((members[x], members[y]));
        }
        return pairs.ToList();
    }
}
