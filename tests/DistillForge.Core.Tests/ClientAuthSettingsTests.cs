using DistillForge.Core.Auth;

namespace DistillForge.Core.Tests;

public class ClientAuthSettingsTests
{
    private const string Tenant = "11111111-1111-1111-1111-111111111111";

    [Fact]
    public void Empty_settings_mean_dev_mode()
    {
        var s = new ClientAuthSettings();
        Assert.False(s.IsEnabled);
        Assert.Empty(s.Validate());
    }

    [Fact]
    public void Blank_strings_still_mean_dev_mode() =>
        Assert.False(new ClientAuthSettings { TenantId = " ", ClientId = "", ApiScope = null }.IsEnabled);

    [Fact]
    public void Complete_settings_are_enabled_and_valid()
    {
        var s = new ClientAuthSettings { TenantId = Tenant, ClientId = "spa", ApiScope = "api://x/access_as_user" };
        Assert.True(s.IsEnabled);
        Assert.Empty(s.Validate());
        Assert.Equal($"https://login.microsoftonline.com/{Tenant}", s.ResolvedAuthority);
    }

    [Fact]
    public void A_partly_filled_in_section_is_reported_not_silently_treated_as_dev_mode()
    {
        var s = new ClientAuthSettings { ClientId = "spa" };
        Assert.True(s.IsEnabled);
        Assert.Equal(2, s.Validate().Count); // tenant and scope missing
    }

    [Fact]
    public void Tenant_must_be_a_guid_unless_an_authority_is_given()
    {
        Assert.NotEmpty(new ClientAuthSettings { TenantId = "contoso", ClientId = "c", ApiScope = "s" }.Validate());
        var custom = new ClientAuthSettings { Authority = "https://example.ciamlogin.com/example.onmicrosoft.com/", ClientId = "c", ApiScope = "s" };
        Assert.Empty(custom.Validate());
        Assert.Equal("https://example.ciamlogin.com/example.onmicrosoft.com", custom.ResolvedAuthority);
    }
}
