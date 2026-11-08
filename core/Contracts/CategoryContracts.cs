namespace DistillForge.Core.Contracts;

public sealed record CategoryDto(Guid Id, string Name, string? Colour, int SortOrder);

/// <summary>Create or rename/recolour a category. A null colour means no accent.</summary>
public sealed record CategoryRequest(string Name, string? Colour);

/// <summary>Moves a note into a category, or back to Uncategorised when CategoryId is null.</summary>
public sealed record MoveActionItemRequest(Guid? CategoryId);

public static class CategoryValidation
{
    public const int MaxName = 100;

    /// <summary>Returns an error message, or null when the request is acceptable. Uniqueness is checked by the caller.</summary>
    public static string? Validate(CategoryRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.Name)) return "Name is required.";
        if (r.Name.Trim().Length > MaxName) return $"Name is too long (max {MaxName} characters).";
        if (!NoteColours.IsValid(r.Colour)) return $"Unknown colour '{r.Colour}'.";
        return null;
    }

    public static bool NameTaken(string name, IEnumerable<string> existing) =>
        existing.Any(e => string.Equals(e.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase));
}
