namespace DistillForge.Core.Extraction;

/// <summary>
/// Merges action points that several overlapping windows reported for the same passage.
/// Two points are duplicates when their evidence sits in the same or adjacent segments AND their
/// titles share most of their content words. Deterministic and model-free; an LLM consolidation
/// pass for paraphrased duplicates further apart is a possible later addition.
/// </summary>
public static class ActionPointDeduplicator
{
    private static readonly HashSet<string> Stopwords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "the", "to", "of", "for", "and", "or", "on", "in", "at", "by", "with", "from", "up", "about", "is", "be",
        "will", "that", "this", "it", "as", "into", "all", "any",
    };

    public static IReadOnlyList<ValidatedActionPoint> Merge(IEnumerable<ValidatedActionPoint> points)
    {
        var kept = new List<ValidatedActionPoint>();
        foreach (var p in points)
        {
            int i = kept.FindIndex(k => IsDuplicate(k, p));
            if (i >= 0) kept[i] = Combine(kept[i], p);
            else kept.Add(p);
        }
        return kept;
    }

    public static bool IsDuplicate(ValidatedActionPoint a, ValidatedActionPoint b) =>
        NearbyEvidence(a, b) && TitlesSimilar(a.Title, b.Title);

    private static bool NearbyEvidence(ValidatedActionPoint a, ValidatedActionPoint b) =>
        a.Evidence.Any(x => b.Evidence.Any(y => Math.Abs(Ordinal(x.SegmentLabel) - Ordinal(y.SegmentLabel)) <= 1));

    private static int Ordinal(string label) =>
        int.TryParse(label.AsSpan(1), out var n) ? n : int.MinValue / 2;

    public static bool TitlesSimilar(string a, string b)
    {
        var wa = Words(a);
        var wb = Words(b);
        if (wa.Count == 0 || wb.Count == 0) return false;
        int common = wa.Intersect(wb).Count();
        int min = Math.Min(wa.Count, wb.Count);
        // Overlap coefficient, with a floor so a single shared word ("budget") never merges two points.
        return common >= Math.Min(2, min) && common >= 0.5 * min;
    }

    private static HashSet<string> Words(string title) =>
        title.ToLowerInvariant()
            .Split([' ', '\t', ',', '.', ';', ':', '-', '"', '\'', '(', ')', '/'], StringSplitOptions.RemoveEmptyEntries)
            .Where(w => !Stopwords.Contains(w))
            .Select(Stem)
            .ToHashSet();

    // Crude plural/-ing/-ed folding so "books"/"book" and "sending"/"send" compare equal.
    private static string Stem(string w)
    {
        foreach (var suffix in new[] { "ing", "ed", "es", "s" })
            if (w.Length > suffix.Length + 2 && w.EndsWith(suffix, StringComparison.Ordinal)) return w[..^suffix.Length];
        return w;
    }

    public static ValidatedActionPoint Combine(ValidatedActionPoint a, ValidatedActionPoint b)
    {
        // Prefer the richer record as the base, then fill its gaps from the other.
        var (main, other) = Richness(b) > Richness(a) ? (b, a) : (a, b);
        var evidence = main.Evidence.ToList();
        foreach (var e in other.Evidence)
            if (!evidence.Any(x => x.Start == e.Start && x.End == e.End)) evidence.Add(e);
        evidence.Sort((x, y) => x.Start.CompareTo(y.Start));

        return new ValidatedActionPoint(
            main.Title,
            main.Summary ?? other.Summary,
            main.Owner ?? other.Owner,
            main.Due ?? other.Due,
            evidence);
    }

    private static int Richness(ValidatedActionPoint p) =>
        (p.Summary?.Length ?? 0) + (p.Owner is null ? 0 : 20) + (p.Due is null ? 0 : 20) + p.Evidence.Count * 5;
}
