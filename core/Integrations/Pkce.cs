using System.Security.Cryptography;
using System.Text;

namespace DistillForge.Core.Integrations;

/// <summary>PKCE (RFC 7636) helpers, plus the random <c>state</c> that ties a callback to the sign-in that started it.</summary>
public static class Pkce
{
    /// <summary>A 64-character URL-safe verifier (the RFC allows 43 to 128).</summary>
    public static string NewVerifier() => Base64Url(RandomNumberGenerator.GetBytes(48));

    public static string NewState() => Base64Url(RandomNumberGenerator.GetBytes(24));

    /// <summary>S256: base64url(SHA-256(verifier)).</summary>
    public static string Challenge(string verifier) => Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
