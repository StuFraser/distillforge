using System.Text.Json.Serialization;

namespace DistillForge.Core.Contracts;

public enum ResponseFormatMode
{
    /// <summary>response_format: json_schema (Ollama, OpenAI, LM Studio).</summary>
    JsonSchema,

    /// <summary>response_format: json_object, for backends without schema support.</summary>
    JsonObject,

    /// <summary>No response_format; rely on the prompt plus validate-and-retry.</summary>
    None,
}

/// <summary>An OpenAI-compatible endpoint. The API key is write-only: only <see cref="HasApiKey"/> is ever returned.</summary>
public sealed record AiProviderDto(
    Guid Id,
    string Name,
    string BaseUrl,
    bool HasApiKey,
    int TimeoutSeconds,
    IReadOnlyList<AiModelDto> Models);

public sealed record AiModelDto(
    Guid Id,
    Guid ProviderId,
    string ModelId,
    string? DisplayName,
    int ContextTokens,
    int ReserveOutput,
    ResponseFormatMode ResponseFormat,
    bool IsDefault);

/// <summary>ApiKey: null keeps the stored key, "" clears it, anything else replaces it.</summary>
public sealed record AiProviderRequest(string Name, string BaseUrl, string? ApiKey, int TimeoutSeconds = 300);

public sealed record AiModelRequest(
    string ModelId,
    string? DisplayName,
    int ContextTokens,
    int ReserveOutput,
    ResponseFormatMode ResponseFormat,
    bool IsDefault);

/// <summary>Lists models from an endpoint. Set ProviderId to reuse that provider's stored key when ApiKey is null.</summary>
public sealed record DiscoverModelsRequest(string BaseUrl, string? ApiKey, Guid? ProviderId = null);

public sealed record DiscoverModelsResponse(bool Ok, IReadOnlyList<string> Models, string? Error);

/// <summary>An entry in the "which model should run this extraction" dropdown.</summary>
public sealed record AiModelChoiceDto(Guid Id, string Label, bool IsDefault);

/// <summary>Body of POST /workspaces/{id}/extract. A null AiModelId means "use the workspace's last model, else the default"; a null MergeLevel means the last run's level, else Medium.</summary>
public sealed record StartExtractionRequest(Guid? AiModelId, MergeLevel? MergeLevel = null);

public static class AiValidation
{
    public const int MaxName = 100, MaxModelId = 200, MaxDisplayName = 100, MaxKey = 2000;
    public const int MinContext = 1024, MaxContext = 4_000_000, MinTimeout = 5, MaxTimeout = 1800;

    /// <summary>Trims whitespace and trailing slashes so URLs compare and concatenate predictably.</summary>
    public static string NormaliseBaseUrl(string? url) => (url ?? "").Trim().TrimEnd('/');

    public static string? ValidateProvider(AiProviderRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.Name)) return "Name is required.";
        if (r.Name.Trim().Length > MaxName) return $"Name is too long (max {MaxName} characters).";
        if (BaseUrlError(r.BaseUrl) is { } e) return e;
        if (r.ApiKey is { Length: > MaxKey }) return "API key is too long.";
        if (r.TimeoutSeconds is < MinTimeout or > MaxTimeout) return $"Timeout must be between {MinTimeout} and {MaxTimeout} seconds.";
        return null;
    }

    public static string? BaseUrlError(string? url)
    {
        var n = NormaliseBaseUrl(url);
        if (n.Length == 0) return "Base URL is required.";
        if (!Uri.TryCreate(n, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            return "Base URL must be an absolute http(s) URL.";
        if (!string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            return "Base URL must not contain a query string or fragment.";
        if (uri.AbsolutePath is "" or "/")
            return "Base URL should include the version path, usually /v1 (e.g. http://host:11434/v1).";
        return null;
    }

    public static string? ValidateModel(AiModelRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.ModelId)) return "Model ID is required.";
        if (r.ModelId.Trim().Length > MaxModelId) return $"Model ID is too long (max {MaxModelId} characters).";
        if (r.DisplayName is { Length: > MaxDisplayName }) return $"Display name is too long (max {MaxDisplayName} characters).";
        if (r.ContextTokens is < MinContext or > MaxContext) return $"Context tokens must be between {MinContext:N0} and {MaxContext:N0}.";
        if (r.ReserveOutput < 256) return "Reserved output must be at least 256 tokens.";
        if (r.ReserveOutput >= r.ContextTokens) return "Reserved output must be smaller than the context window.";
        if (!Enum.IsDefined(r.ResponseFormat)) return "Unknown response format.";
        return null;
    }

    public static string ModelLabel(string providerName, string modelId, string? displayName) =>
        $"{(string.IsNullOrWhiteSpace(displayName) ? modelId : displayName)} · {providerName}";
}
