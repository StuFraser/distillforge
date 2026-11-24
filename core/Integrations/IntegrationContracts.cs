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
            if (f.AllowedHosts is not null && !HostAllowed(v, f.AllowedHosts)) return $"{f.Label}: host must be {string.Join(" or ", f.AllowedHosts.Select(h => h.StartsWith('.') ? "*" + h : h))}.";
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

    /// <summary>True when the URL's host equals an allowed host or ends with an allowed ".suffix".</summary>
    public static bool HostAllowed(string url, IReadOnlyList<string> allowed)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) return false;
        var host = u.IdnHost.ToLowerInvariant();
        return allowed.Any(a => a.StartsWith('.') ? host.EndsWith(a, StringComparison.Ordinal) && host.Length > a.Length : host == a);
    }

    private static string? UrlError(string v) =>
        Uri.TryCreate(v, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(u.Query) && string.IsNullOrEmpty(u.Fragment)
            ? null : "must be an https:// URL with no query string.";
}
