using DistillForge.Core.Segmentation;

namespace DistillForge.Core.Extraction;

public sealed record ValidatedActionPoint(
    string Title,
    string? Summary,
    string? Owner,
    string? Due,
    IReadOnlyList<ResolvedEvidence> Evidence);

public sealed record ValidationOutcome(IReadOnlyList<ValidatedActionPoint> ActionPoints, IReadOnlyList<string> Warnings);

/// <summary>
/// Guards against hallucination: evidence must reference real segment IDs and is
/// resolved to offsets in code. Action points with no valid evidence are dropped.
/// Runs on every extraction, single-window or not.
/// </summary>
public static class ExtractionValidator
{
    public static ValidationOutcome Validate(ExtractionResponse response, IReadOnlyList<Segment> segments)
    {
        var byLabel = segments.ToDictionary(s => s.Label, StringComparer.OrdinalIgnoreCase);
        var items = new List<ValidatedActionPoint>();
        var warnings = new List<string>();

        foreach (var ap in response.ActionPoints)
        {
            var title = ap.Title?.Trim();
            if (string.IsNullOrEmpty(title))
            {
                warnings.Add($"Dropped action point {ap.Id ?? "?"}: missing title.");
                continue;
            }

            var evidence = new List<ResolvedEvidence>();
            foreach (var ev in ap.Evidence)
            {
                var label = NormaliseSegmentId(ev.SegmentId);
                if (label is null || !byLabel.TryGetValue(label, out var segment))
                {
                    warnings.Add($"'{title}': unknown segment id '{ev.SegmentId}'.");
                    continue;
                }

                var resolved = QuoteResolver.Resolve(segment, ev.Quote);
                if (resolved.Quality == MatchQuality.Segment)
                {
                    warnings.Add($"'{title}': quote not found in {segment.Label}; highlighting whole segment.");
                }
                if (!evidence.Any(e => e.Start == resolved.Start && e.End == resolved.End))
                {
                    evidence.Add(resolved);
                }
            }

            if (evidence.Count == 0)
            {
                warnings.Add($"Dropped '{title}': no valid evidence.");
                continue;
            }

            evidence.Sort((a, b) => a.Start.CompareTo(b.Start));
            items.Add(new ValidatedActionPoint(title, Blank(ap.Summary), Blank(ap.Owner), Blank(ap.Due), evidence));
        }

        return new ValidationOutcome(items, warnings);
    }

    /// <summary>Accepts "S12", "s12", "[S12]" or "12".</summary>
    public static string? NormaliseSegmentId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        var t = id.Trim().Trim('[', ']').Trim();
        if (t.Length == 0) return null;
        if (t.All(char.IsDigit)) return "S" + t;
        if ((t[0] is 'S' or 's') && t.Length > 1 && t.Skip(1).All(char.IsDigit)) return "S" + t[1..];
        return null;
    }

    private static string? Blank(string? s)
    {
        var t = s?.Trim();
        return string.IsNullOrEmpty(t) || t.Equals("null", StringComparison.OrdinalIgnoreCase) ? null : t;
    }
}
