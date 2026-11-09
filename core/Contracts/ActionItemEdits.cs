namespace DistillForge.Core.Contracts;

/// <summary>Colours a note can be given. A null colour means "automatic": pink, or yellow when the note has a deadline.</summary>
public static class NoteColours
{
    public const string Pink = "pink";
    public const string Yellow = "yellow";

    public static readonly IReadOnlyList<string> Keys = [Pink, Yellow, "lavender", "orange", "green", "blue"];

    public static bool IsValid(string? key) => key is null || Keys.Contains(key);

    public static string DefaultFor(bool hasDeadline) => hasDeadline ? Yellow : Pink;

    public static string Label(string key) => char.ToUpperInvariant(key[0]) + key[1..];
}

/// <summary>Full replacement of a note's editable fields. Blank optional fields are stored as null.</summary>
public sealed record UpdateActionItemRequest(string Title, string? Summary, string? Owner, string? Due, string? Colour);

/// <summary>A manually added note. It goes into <see cref="CategoryId"/>, or Uncategorised when null.</summary>
/// <param name="SourceStart">Optional passage of the source text (offsets, End exclusive) the note was created from; becomes its evidence.</param>
public sealed record CreateActionItemRequest(string Title, string? Summary, string? Owner, string? Due, string? Colour, Guid? CategoryId,
    int? SourceStart = null, int? SourceEnd = null);

public static class ActionItemValidation
{
    public const int MaxTitle = 300, MaxSummary = 4000, MaxOwner = 200, MaxDue = 100;

    /// <summary>Returns an error message, or null when the request is acceptable.</summary>
    public static string? Validate(CreateActionItemRequest r) =>
        Validate(new UpdateActionItemRequest(r.Title, r.Summary, r.Owner, r.Due, r.Colour));

    public static string? Validate(UpdateActionItemRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.Title)) return "Title is required.";
        if (r.Title.Trim().Length > MaxTitle) return $"Title is too long (max {MaxTitle} characters).";
        if (r.Summary is { Length: > MaxSummary }) return $"Summary is too long (max {MaxSummary} characters).";
        if (r.Owner is { Length: > MaxOwner }) return $"Owner is too long (max {MaxOwner} characters).";
        if (r.Due is { Length: > MaxDue }) return $"Due date is too long (max {MaxDue} characters).";
        if (!NoteColours.IsValid(r.Colour)) return $"Unknown colour '{r.Colour}'.";
        return null;
    }
}
