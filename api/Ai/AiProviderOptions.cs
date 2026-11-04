namespace DistillForge.Api.Ai;

public enum ResponseFormatMode
{
    /// <summary>response_format: json_schema (Ollama, OpenAI, LM Studio).</summary>
    JsonSchema,

    /// <summary>response_format: json_object, for backends without schema support.</summary>
    JsonObject,

    /// <summary>No response_format; rely on the prompt plus validate-and-retry.</summary>
    None,
}

/// <summary>
/// Any OpenAI-compatible chat-completions endpoint (Ollama, LM Studio, OpenAI, Azure, OpenRouter...).
/// Bound from the "AiProvider" config section. Per-user provider config comes later.
/// </summary>
public sealed class AiProviderOptions
{
    public const string Section = "AiProvider";

    /// <summary>Base URL including the version path, e.g. http://mini:11434/v1.</summary>
    public string BaseUrl { get; set; } = "";
    public string Model { get; set; } = "";
    /// <summary>Dummy value for Ollama.</summary>
    public string? ApiKey { get; set; }

    /// <summary>User-set context window; there's no standard way to query it.</summary>
    public int ContextTokens { get; set; } = 8192;
    public int ReserveOutput { get; set; } = 2048;
    public double SafetyMargin { get; set; } = 0.1;

    public ResponseFormatMode ResponseFormat { get; set; } = ResponseFormatMode.JsonSchema;
    public int TimeoutSeconds { get; set; } = 300;

    /// <summary>Tokens available for the prompt in a single window.</summary>
    public int PromptBudget => (int)((ContextTokens - ReserveOutput) * (1 - SafetyMargin));
}
