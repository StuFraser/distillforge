namespace DistillForge.Core.Integrations;

/// <summary>
/// A saved connection. Secret values are never returned: <see cref="SecretsSet"/> only says which secret fields have a value.
/// </summary>
public sealed record IntegrationDto(
    Guid Id,
    IntegrationPlatform Platform,
    string Name,
    IReadOnlyDictionary<string, string> Settings,
    IReadOnlyList<string> SecretsSet,
    DateTimeOffset CreatedAt);

/// <summary>
/// Values holds every field by key. For secret fields: absent or null keeps the stored value, "" clears it, anything else
/// replaces it. Platform can't change after creation (the update endpoint rejects a different one).
/// </summary>
public sealed record IntegrationRequest(IntegrationPlatform Platform, string Name, Dictionary<string, string?> Values);

public static class IntegrationValidation
{
    public const int MaxName = 100, MaxValue = 500, MaxSecret = 2000;

    /// <param name="existingSecrets">Secret keys that already have a stored value (empty when creating).</param>
    public static string? Validate(IntegrationRequest r, IReadOnlyCollection<string>? existingSecrets = null)
    {
        if (!Enum.IsDefined(r.Platform)) return "Unknown platform.";
        if (string.IsNullOrWhiteSpace(r.Name)) return "Name is required.";
        if (r.Name.Trim().Length > MaxName) return $"Name is too long (max {MaxName} characters).";
        if (r.Values is null) return "Values are required.";

        var def = IntegrationCatalog.Get(r.Platform);
        var unknown = r.Values.Keys.FirstOrDefault(k => def.Fields.All(f => f.Key != k));
        if (unknown is not null) return $"'{unknown}' isn't a setting of {def.Name}.";

        foreach (var f in def.Fields)
        {
            r.Values.TryGetValue(f.Key, out var raw);
            var v = raw?.Trim();

            if (f.IsSecret)
            {
                var stays = raw is null && existingSecrets?.Contains(f.Key) == true;
                var cleared = raw is not null && v!.Length == 0;
                if (f.Required && !stays && (raw is null || cleared)) return $"{f.Label} is required.";
                if (v is { Length: > MaxSecret }) return $"{f.Label} is too long.";
                continue;
            }

            if (string.IsNullOrEmpty(v))
            {
                if (f.Required && f.Default is null) return $"{f.Label} is required.";
                continue;
            }
            if (v.Length > MaxValue) return $"{f.Label} is too long (max {MaxValue} characters).";
            if (f.Kind == FieldKind.Url && UrlError(v) is { } urlError) return $"{f.Label}: {urlError}";
            if (f.Kind == FieldKind.Select && f.Options?.Contains(v) != true) return $"{f.Label} must be one of: {string.Join(", ", f.Options ?? [])}.";
        }
        return null;
    }

    /// <summary>Non-secret values to store: trimmed, with blanks replaced by the field default (or dropped).</summary>
    public static Dictionary<string, string> CleanSettings(IntegrationRequest r)
    {
        var result = new Dictionary<string, string>();
        foreach (var f in IntegrationCatalog.Get(r.Platform).Fields.Where(f => !f.IsSecret))
        {
            r.Values.TryGetValue(f.Key, out var raw);
            var v = raw?.Trim();
            if (string.IsNullOrEmpty(v)) v = f.Default;
            if (string.IsNullOrEmpty(v)) continue;
            result[f.Key] = f.Kind == FieldKind.Url ? v.TrimEnd('/') : v;
        }
        return result;
    }

    private static string? UrlError(string v) =>
        Uri.TryCreate(v, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(u.Query) && string.IsNullOrEmpty(u.Fragment)
            ? null : "must be an https:// URL with no query string.";
}
