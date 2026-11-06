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

public sealed record ConsolidationOutcome(IReadOnlyList<ValidatedActionPoint> ActionPoints, int MergedCount, IReadOnlyList<string> Warnings);

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
        return new ConsolidationOutcome(result, absorbed.Count, warnings);
    }
}
