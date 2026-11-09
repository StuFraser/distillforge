namespace DistillForge.Core.Contracts;

/// <summary>Merge a note INTO the target: the note becomes a hidden child that follows the target.</summary>
public sealed record MergeActionItemRequest(Guid TargetId);

/// <summary>Dismiss the possible-duplicate flag between a note and another.</summary>
public sealed record NotDuplicateRequest(Guid OtherId);

public sealed record NoteNode(Guid Id, Guid? MergedIntoId);

public sealed record DuplicateLink(Guid A, Guid B, bool Dismissed);

/// <summary>Pure rules for merged notes and possible-duplicate flags (one level of merging only).</summary>
public static class NoteGraph
{
    /// <summary>The top-level note a note belongs to (itself if it isn't merged).</summary>
    public static Guid Root(Guid id, IReadOnlyDictionary<Guid, Guid?> parentOf)
    {
        // Merging is one level deep, but guard against bad data (cycles) anyway.
        for (int hops = 0; hops < 16; hops++)
        {
            if (!parentOf.TryGetValue(id, out var p) || p is null) return id;
            id = p.Value;
        }
        return id;
    }

    /// <summary>
    /// For each top-level note, the other top-level notes it may duplicate. Links are followed through merges
    /// (a flag on a merged child counts for its parent), ignore dismissed links, and ignore pairs that ended up together.
    /// Merged notes get nothing: they're hidden.
    /// </summary>
    public static IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> PossibleDuplicates(
        IEnumerable<NoteNode> notes, IEnumerable<DuplicateLink> links)
    {
        var list = notes.ToList();
        var parentOf = list.ToDictionary(n => n.Id, n => n.MergedIntoId);
        var result = list.Where(n => n.MergedIntoId is null).ToDictionary(n => n.Id, _ => new HashSet<Guid>());

        foreach (var link in links.Where(l => !l.Dismissed))
        {
            if (!parentOf.ContainsKey(link.A) || !parentOf.ContainsKey(link.B)) continue;
            var ra = Root(link.A, parentOf);
            var rb = Root(link.B, parentOf);
            if (ra == rb || !result.ContainsKey(ra) || !result.ContainsKey(rb)) continue;
            result[ra].Add(rb);
            result[rb].Add(ra);
        }
        return result.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<Guid>)kv.Value.Order().ToList());
    }

    /// <summary>Returns an error, or null when merging <paramref name="source"/> into <paramref name="target"/> is allowed.</summary>
    public static string? ValidateMerge(Guid source, Guid target, IReadOnlyDictionary<Guid, Guid?> parentOf)
    {
        if (!parentOf.ContainsKey(source) || !parentOf.ContainsKey(target)) return "Note not found in this workspace.";
        if (source == target) return "A note can't be merged into itself.";
        if (Root(target, parentOf) == source) return "A note can't be merged into one of its own merged notes.";
        return null;
    }
}
