using System.Text.Json;
using System.Text.Json.Serialization;
using DistillForge.Core.Contracts;

namespace DistillForge.Bench;

public sealed class BenchConfig
{
    public int RunsPerCell { get; set; } = 1;
    public int RunTimeoutMinutes { get; set; } = 15;
    public List<ModelEntry> Models { get; set; } = [];
    public List<ProfileEntry> Profiles { get; set; } = [];

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static BenchConfig Load(string path)
    {
        var cfg = JsonSerializer.Deserialize<BenchConfig>(File.ReadAllText(path), Json)
                  ?? throw new InvalidOperationException($"{path} is empty.");
        if (cfg.Models.Count == 0) throw new InvalidOperationException("Config needs at least one model.");
        if (cfg.Profiles.Count == 0) cfg.Profiles.Add(new ProfileEntry { Name = "default", ContextTokens = 8192, ReserveOutput = 2048 });
        foreach (var m in cfg.Models)
            if (string.IsNullOrWhiteSpace(m.Name) || string.IsNullOrWhiteSpace(m.BaseUrl) || string.IsNullOrWhiteSpace(m.Model))
                throw new InvalidOperationException("Every model needs name, baseUrl and model.");
        return cfg;
    }
}

public sealed class ModelEntry
{
    public string Name { get; set; } = "";
    public string BaseUrl { get; set; } = "";
    public string Model { get; set; } = "";
    /// <summary>Literal key (fine for Ollama). Prefer <see cref="ApiKeyEnv"/> for hosted providers so keys never reach a file.</summary>
    public string? ApiKey { get; set; }
    public string? ApiKeyEnv { get; set; }
    public ResponseFormatMode ResponseFormat { get; set; } = ResponseFormatMode.JsonSchema;
    public int TimeoutSeconds { get; set; } = 300;

    public string? ResolveKey() =>
        !string.IsNullOrWhiteSpace(ApiKeyEnv) ? Environment.GetEnvironmentVariable(ApiKeyEnv) : ApiKey;
}

public sealed class ProfileEntry
{
    public string Name { get; set; } = "";
    public int ContextTokens { get; set; } = 8192;
    public int ReserveOutput { get; set; } = 2048;
}
