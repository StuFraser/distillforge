using System.Security.Cryptography;
using System.Text;

namespace DistillForge.Data.Security;

/// <summary>
/// AES-256-GCM encryption for secrets stored in the database (AI provider API keys).
/// The key comes from configuration (Encryption__Key: 32 random bytes, base64). Output format:
/// "v1:" + base64(nonce[12] | tag[16] | ciphertext). Rotating the key invalidates stored secrets.
/// </summary>
public sealed class SecretProtector
{
    private const string Prefix = "v1:";
    private const int NonceSize = 12, TagSize = 16;
    private readonly byte[]? _key;

    public SecretProtector(string? base64Key)
    {
        if (string.IsNullOrWhiteSpace(base64Key)) return;
        try { _key = Convert.FromBase64String(base64Key.Trim()); }
        catch (FormatException) { throw new InvalidOperationException("Encryption__Key must be base64 (generate with: openssl rand -base64 32)."); }
        if (_key.Length != 32) throw new InvalidOperationException("Encryption__Key must decode to exactly 32 bytes (generate with: openssl rand -base64 32).");
    }

    public bool IsConfigured => _key is not null;

    public string Protect(string plaintext)
    {
        var key = _key ?? throw new SecretProtectionException("Encryption__Key is not configured, so API keys can't be stored. Generate one with `openssl rand -base64 32` and add it to api/local.settings.json.");
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var data = Encoding.UTF8.GetBytes(plaintext);
        var cipher = new byte[data.Length];
        var tag = new byte[TagSize];
        using (var aes = new AesGcm(key, TagSize)) aes.Encrypt(nonce, data, cipher, tag);

        var packed = new byte[NonceSize + TagSize + cipher.Length];
        nonce.CopyTo(packed, 0);
        tag.CopyTo(packed, NonceSize);
        cipher.CopyTo(packed, NonceSize + TagSize);
        return Prefix + Convert.ToBase64String(packed);
    }

    public string Unprotect(string protectedValue)
    {
        var key = _key ?? throw new SecretProtectionException("Encryption__Key is not configured, so stored API keys can't be read.");
        try
        {
            if (!protectedValue.StartsWith(Prefix)) throw new FormatException();
            var packed = Convert.FromBase64String(protectedValue[Prefix.Length..]);
            if (packed.Length < NonceSize + TagSize) throw new FormatException();
            var nonce = packed.AsSpan(0, NonceSize);
            var tag = packed.AsSpan(NonceSize, TagSize);
            var cipher = packed.AsSpan(NonceSize + TagSize);
            var plain = new byte[cipher.Length];
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(nonce, cipher, tag, plain);
            return Encoding.UTF8.GetString(plain);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            throw new SecretProtectionException("A stored API key couldn't be decrypted (was Encryption__Key changed?). Re-enter the key for that provider.");
        }
    }
}

public sealed class SecretProtectionException(string message) : Exception(message);
