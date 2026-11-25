using System.Security.Cryptography;

namespace DistillForge.Core.Codes;

public enum ShortCodeKind { Workspace, Note }

/// <summary>
/// Short, human-friendly identifiers for workspaces and notes (W-4K7Q2M, N-9F2M8X). Stored bare (6 characters of Crockford
/// base32, so no I, L, O or U); the W-/N- prefix is added for display and says which kind it is. Generated once and never
/// changed. They are for people to quote and look up, not a security boundary: lookups are always scoped to the signed-in user.
/// </summary>
public static class ShortCode
{
    public const int Length = 6;
    public const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    public static string New()
    {
        Span<char> chars = stackalloc char[Length];
        for (var i = 0; i < Length; i++) chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        return new string(chars);
    }

    public static string ForWorkspace(string code) => "W-" + code;
    public static string ForNote(string code) => "N-" + code;

    /// <summary>
    /// Accepts what people type or paste: any case, spaces, the dash optional, and the look-alikes I/L and O read as 1 and 0.
    /// </summary>
    public static bool TryParse(string? input, out ShortCodeKind kind, out string code)
    {
        kind = default;
        code = "";
        if (string.IsNullOrWhiteSpace(input)) return false;

        var s = new string(input.Where(c => !char.IsWhiteSpace(c) && c != '-').ToArray()).ToUpperInvariant();
        if (s.Length != Length + 1) return false;
        kind = s[0] switch { 'W' => ShortCodeKind.Workspace, 'N' => ShortCodeKind.Note, _ => (ShortCodeKind)(-1) };
        if (!Enum.IsDefined(kind)) return false;

        var body = s[1..].Replace('I', '1').Replace('L', '1').Replace('O', '0');
        if (body.Any(c => !Alphabet.Contains(c))) return false;
        code = body;
        return true;
    }
}
