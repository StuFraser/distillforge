using DistillForge.Core.Export;
using DistillForge.Core.Integrations;

namespace DistillForge.Api.Export;

/// <summary>A saved connection with its secrets decrypted. Lives only for the duration of a request; never logged or returned.</summary>
public sealed record TargetConnection(IReadOnlyDictionary<string, string> Settings, IReadOnlyDictionary<string, string> Secrets)
{
    public string Setting(string key) => Settings.GetValueOrDefault(key) ?? "";
    public string Secret(string key) => Secrets.GetValueOrDefault(key) ?? "";
}

/// <summary>One implementation per platform; the push endpoints pick by <see cref="Platform"/>. No platform logic belongs outside these.</summary>
public interface IExportTarget
{
    IntegrationPlatform Platform { get; }

    /// <summary>What the level above an item is called ("Project"), or null when the platform has none.</summary>
    string? ScopeLabel { get; }

    /// <summary>
    /// Choices for the push dialog. With no <paramref name="scope"/> it also lists the scopes and picks one; with one it only
    /// lists that scope's item types. Failures come back in <see cref="PushOptionsDto.Error"/> rather than throwing.
    /// </summary>
    Task<PushOptionsDto> GetOptionsAsync(TargetConnection connection, string? scope, CancellationToken ct);

    /// <summary>
    /// For platforms whose credentials expire (OAuth): returns the full, renewed set of secrets to store, or null when the stored
    /// ones are still good. Throws <see cref="ExportAuthException"/> when they can't be renewed and the user must reconnect.
    /// </summary>
    Task<IReadOnlyDictionary<string, string>?> RefreshSecretsAsync(TargetConnection connection, CancellationToken ct) =>
        Task.FromResult<IReadOnlyDictionary<string, string>?>(null);

    /// <summary>Creates one item. A failure specific to this note returns Ok=false; a failure that would hit every note (bad token) throws <see cref="ExportAuthException"/>.</summary>
    Task<PushOutcome> PushAsync(TargetConnection connection, ExportItem item, PushChoices choices, CancellationToken ct);
}

/// <summary>The platform rejected the credentials or the connection's target doesn't exist, so trying further notes is pointless.</summary>
public sealed class ExportAuthException(string message) : Exception(message);
