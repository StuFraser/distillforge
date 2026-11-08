using System.Security.Cryptography;
using DistillForge.Data.Security;

namespace DistillForge.Core.Tests;

public class SecretProtectorTests
{
    private static string NewKey() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    [Fact]
    public void RoundTrips()
    {
        var p = new SecretProtector(NewKey());
        var enc = p.Protect("sk-secret-123");
        Assert.DoesNotContain("secret", enc);
        Assert.Equal("sk-secret-123", p.Unprotect(enc));
    }

    [Fact]
    public void SamePlaintextEncryptsDifferentlyEachTime()
    {
        var p = new SecretProtector(NewKey());
        Assert.NotEqual(p.Protect("x"), p.Protect("x"));
    }

    [Fact]
    public void WrongKeyOrTamperingIsRejected()
    {
        var enc = new SecretProtector(NewKey()).Protect("sk-1");
        Assert.Throws<SecretProtectionException>(() => new SecretProtector(NewKey()).Unprotect(enc));

        var p = new SecretProtector(NewKey());
        var good = p.Protect("sk-1");
        var bytes = Convert.FromBase64String(good[3..]);
        bytes[^1] ^= 1;
        Assert.Throws<SecretProtectionException>(() => p.Unprotect("v1:" + Convert.ToBase64String(bytes)));
        Assert.Throws<SecretProtectionException>(() => p.Unprotect("garbage"));
    }

    [Fact]
    public void UnconfiguredCannotProtect()
    {
        var p = new SecretProtector(null);
        Assert.False(p.IsConfigured);
        Assert.Throws<SecretProtectionException>(() => p.Protect("x"));
    }

    [Theory]
    [InlineData("not base64!!")]
    [InlineData("AAAA")]
    public void BadKeyFailsFast(string key) => Assert.Throws<InvalidOperationException>(() => new SecretProtector(key));
}
