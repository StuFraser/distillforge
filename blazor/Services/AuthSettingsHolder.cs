using DistillForge.Core.Auth;

namespace DistillForge.Blazor.Services;

/// <summary>The front end's sign-in settings, validated at startup, for components to ask "is real sign-in on?".</summary>
public sealed class AuthSettingsHolder(ClientAuthSettings settings)
{
    public ClientAuthSettings Settings { get; } = settings;
}
