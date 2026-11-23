using System.Security.Cryptography;
using DistillForge.Api.Auth;
using DistillForge.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace DistillForge.Api.Tests;

public class AuthOptionsTests
{
    private const string Tenant = "11111111-1111-1111-1111-111111111111";

    [Fact]
    public void Entra_mode_needs_tenant_and_client() =>
        Assert.NotEmpty(new AuthOptions().Validate(isDevelopment: true));

    [Fact]
    public void Entra_mode_with_tenant_and_client_is_valid_everywhere()
    {
        var o = new AuthOptions { TenantId = Tenant, ClientId = "c" };
        Assert.Empty(o.Validate(isDevelopment: false));
        Assert.Empty(o.Validate(isDevelopment: true));
    }

    [Fact]
    public void Tenant_must_be_a_guid() =>
        Assert.NotEmpty(new AuthOptions { TenantId = "contoso", ClientId = "c" }.Validate(true));

    [Fact]
    public void Dev_mode_is_refused_outside_Development()
    {
        var o = new AuthOptions { Mode = AuthMode.Dev };
        Assert.Empty(o.Validate(isDevelopment: true));
        Assert.Contains("Development", Assert.Single(o.Validate(isDevelopment: false)));
    }
}

public class JwtAuthenticatorTests
{
    private const string Tenant = "11111111-1111-1111-1111-111111111111";
    private const string Client = "22222222-2222-2222-2222-222222222222";
    private static readonly string Issuer = JwtAuthenticator.Authority(Tenant);

    private static readonly RsaSecurityKey SigningKey = new(RSA.Create(2048)) { KeyId = "k1" };
    private static readonly RsaSecurityKey OtherKey = new(RSA.Create(2048)) { KeyId = "k1" };

    private sealed class FixedMetadata(params SecurityKey[] keys) : IConfigurationManager<OpenIdConnectConfiguration>
    {
        public Task<OpenIdConnectConfiguration> GetConfigurationAsync(CancellationToken cancel)
        {
            var c = new OpenIdConnectConfiguration { Issuer = Issuer };
            foreach (var k in keys) c.SigningKeys.Add(k);
            return Task.FromResult(c);
        }
        public void RequestRefresh() { }
    }

    private static JwtAuthenticator Authenticator(string? audience = null) => new(
        Options.Create(new AuthOptions { TenantId = Tenant, ClientId = Client, Audience = audience }),
        new FixedMetadata(SigningKey), NullLogger<JwtAuthenticator>.Instance);

    private static string Token(Action<Dictionary<string, object>>? claims = null, string? issuer = null, string? audience = null,
        SecurityKey? key = null, DateTime? expires = null)
    {
        var c = new Dictionary<string, object> { ["tid"] = Tenant, ["oid"] = "ABCDEF00-0000-0000-0000-000000000001", ["scp"] = "access_as_user", ["name"] = "Sam Example" };
        claims?.Invoke(c);
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer ?? Issuer,
            Audience = audience ?? Client,
            Claims = c,
            NotBefore = DateTime.UtcNow.AddMinutes(-10),
            Expires = expires ?? DateTime.UtcNow.AddMinutes(50),
            SigningCredentials = new SigningCredentials(key ?? SigningKey, SecurityAlgorithms.RsaSha256),
        });
    }

    private static Task<AuthResult> Auth(string token, JwtAuthenticator? a = null) =>
        (a ?? Authenticator()).AuthenticateAsync("Bearer " + token, CancellationToken.None);

    [Fact]
    public async Task A_valid_token_yields_the_user_with_lowercased_ids()
    {
        var r = await Auth(Token());
        Assert.True(r.Succeeded, r.Failure);
        Assert.Equal(new AuthenticatedUser(Tenant, "abcdef00-0000-0000-0000-000000000001", "Sam Example"), r.User);
    }

    [Fact]
    public async Task The_application_id_uri_audience_is_accepted() =>
        Assert.True((await Auth(Token(audience: $"api://{Client}"))).Succeeded);

    [Fact]
    public async Task A_configured_extra_audience_is_accepted() =>
        Assert.True((await Auth(Token(audience: "https://api.example"), Authenticator("https://api.example"))).Succeeded);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Basic abc")]
    [InlineData("Bearer ")]
    [InlineData("Bearer not-a-jwt")]
    public async Task Missing_or_malformed_headers_are_rejected(string? header) =>
        Assert.False((await Authenticator().AuthenticateAsync(header, CancellationToken.None)).Succeeded);

    [Fact]
    public async Task A_token_signed_by_another_key_is_rejected() =>
        Assert.False((await Auth(Token(key: OtherKey))).Succeeded);

    [Fact]
    public async Task Wrong_issuer_is_rejected() =>
        Assert.False((await Auth(Token(issuer: JwtAuthenticator.Authority("33333333-3333-3333-3333-333333333333")))).Succeeded);

    [Fact]
    public async Task Wrong_audience_is_rejected() =>
        Assert.False((await Auth(Token(audience: "someone-else"))).Succeeded);

    [Fact]
    public async Task An_expired_token_is_rejected() =>
        Assert.False((await Auth(Token(expires: DateTime.UtcNow.AddMinutes(-30)))).Succeeded);

    [Fact]
    public async Task A_token_for_another_tenant_is_rejected() =>
        Assert.False((await Auth(Token(c => c["tid"] = "33333333-3333-3333-3333-333333333333"))).Succeeded);

    [Fact]
    public async Task A_token_without_the_required_scope_is_rejected() =>
        Assert.False((await Auth(Token(c => c["scp"] = "something.else"))).Succeeded);

    [Fact]
    public async Task An_app_only_token_without_scope_or_user_is_rejected() =>
        Assert.False((await Auth(Token(c => { c.Remove("scp"); c.Remove("oid"); c["roles"] = new[] { "Task.Read" }; }))).Succeeded);

    [Fact]
    public async Task A_token_without_an_object_id_is_rejected() =>
        Assert.False((await Auth(Token(c => c.Remove("oid")))).Succeeded);

    [Fact]
    public async Task An_unsigned_token_is_rejected()
    {
        var unsigned = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer, Audience = Client, Expires = DateTime.UtcNow.AddMinutes(5),
            Claims = new Dictionary<string, object> { ["tid"] = Tenant, ["oid"] = "x", ["scp"] = "access_as_user" },
        });
        Assert.False((await Auth(unsigned)).Succeeded);
    }
}

public class UserProvisionerTests : IDisposable
{
    private readonly TestDb _db = new();
    public void Dispose() => _db.Dispose();

    private UserProvisioner Provisioner(DistillForge.Data.DistillForgeDbContext db, bool claim = false) =>
        new(db, Options.Create(new AuthOptions { ClaimOrphanedData = claim }));

    [Fact]
    public async Task First_sign_in_creates_the_user_and_later_ones_reuse_it()
    {
        var who = new AuthenticatedUser("t1", "o1", "Sam");
        await using var db1 = _db.NewContext();
        var first = await Provisioner(db1).EnsureUserAsync(who, default);
        await using var db2 = _db.NewContext();
        var second = await Provisioner(db2).EnsureUserAsync(who, default);

        Assert.Equal(first, second);
        await using var db = _db.NewContext();
        Assert.Single(await db.AppUsers.Where(u => u.ObjectId == "o1").ToListAsync());
    }

    [Fact]
    public async Task The_same_object_id_in_a_different_tenant_is_a_different_user()
    {
        await using var db = _db.NewContext();
        var a = await Provisioner(db).EnsureUserAsync(new AuthenticatedUser("t1", "o1", null), default);
        var b = await Provisioner(db).EnsureUserAsync(new AuthenticatedUser("t2", "o1", null), default);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public async Task A_changed_display_name_is_kept_up_to_date()
    {
        await using var db = _db.NewContext();
        var id = await Provisioner(db).EnsureUserAsync(new AuthenticatedUser("t1", "o1", "Old"), default);
        await Provisioner(db).EnsureUserAsync(new AuthenticatedUser("t1", "o1", "New"), default);
        Assert.Equal("New", (await db.AppUsers.FindAsync(id))!.DisplayName);
    }

    [Fact]
    public async Task Orphaned_rows_are_claimed_by_the_first_new_user_only_when_enabled()
    {
        Guid orphanWorkspace;
        await using (var seed = _db.NewContext())
        {
            var w = new Workspace { Id = Guid.NewGuid(), OwnerId = TestDb.UserA, Title = "old", SourceText = "old", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
            seed.Workspaces.Add(w);
            seed.AiProviders.Add(new AiProvider { Id = Guid.NewGuid(), OwnerId = TestDb.UserA, Name = "p", BaseUrl = "http://x/v1" });
            await seed.SaveChangesAsync();
            await seed.Database.ExecuteSqlRawAsync("UPDATE Workspaces SET OwnerId = NULL; UPDATE AiProviders SET OwnerId = NULL");
            orphanWorkspace = w.Id;
        }

        await using (var off = _db.NewContext())
        {
            var id = await Provisioner(off, claim: false).EnsureUserAsync(new AuthenticatedUser("t", "new1", null), default);
            Assert.Null((await off.Workspaces.IgnoreQueryFilters().SingleAsync()).OwnerId);
            Assert.NotEqual(Guid.Empty, id);
        }

        await using var on = _db.NewContext();
        var claimer = await Provisioner(on, claim: true).EnsureUserAsync(new AuthenticatedUser("t", "new2", null), default);
        Assert.Equal(claimer, (await on.Workspaces.IgnoreQueryFilters().SingleAsync(w => w.Id == orphanWorkspace)).OwnerId);
        Assert.Equal(claimer, (await on.AiProviders.IgnoreQueryFilters().SingleAsync()).OwnerId);
    }
}
