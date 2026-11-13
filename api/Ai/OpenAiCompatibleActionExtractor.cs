using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using DistillForge.Core.Contracts;
using DistillForge.Core.Extraction;
using DistillForge.Core.Segmentation;
using Microsoft.Extensions.Logging;

namespace DistillForge.Api.Ai;

public sealed class ExtractionFailedException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Plain OpenAI-style chat completions + response_format. No backend-specific code paths,
/// so the same client works against Ollama, LM Studio or a hosted provider.
/// One call = one window; windowing and merging live in ExtractionRunner.
/// </summary>
public sealed class OpenAiCompatibleActionExtractor(
    HttpClient http,
    AiProviderOptions options,
    ILogger<OpenAiCompatibleActionExtractor> logger) : IActionExtractor
{
    private readonly AiProviderOptions _opt = options;

    public int PromptBudgetTokens => _opt.PromptBudget;

    public async Task<ExtractionResult> ExtractAsync(ExtractionWindow window, CancellationToken cancellationToken = default)
    {
        var segments = window.Segments;
        if (string.IsNullOrWhiteSpace(_opt.BaseUrl) || string.IsNullOrWhiteSpace(_opt.Model))
            throw new ExtractionFailedException("The AI provider has no base URL or model configured.");

        int estimate = ExtractionPrompt.EstimateTokens(segments);
        if (estimate > _opt.PromptBudget)
        {
            throw new ExtractionFailedException(
                $"Window is ~{estimate} tokens but the prompt budget is {_opt.PromptBudget} " +
                $"(context {_opt.ContextTokens} - reserved output {_opt.ReserveOutput}, less {_opt.SafetyMargin:P0}). " +
                "A single segment is larger than the budget; raise the model's context tokens in AI settings.");
        }

        var messages = new JsonArray
        {
            new JsonObject { ["role"] = "system", ["content"] = ExtractionPrompt.System },
            new JsonObject { ["role"] = "user", ["content"] = ExtractionPrompt.BuildUserMessage(window) },
        };

        var (response, completion, warnings) = await CompleteJsonAsync<ExtractionResponse>(
            messages, "action_points", ExtractionPrompt.Schema(), cancellationToken);
        return Result(response, completion, estimate, warnings);
    }

    public int EstimateConsolidationTokens(IReadOnlyList<ConsolidationCandidate> candidates) =>
        ConsolidationPrompt.EstimateTokens(candidates);

    public async Task<ConsolidationResult> ConsolidateAsync(IReadOnlyList<ConsolidationCandidate> candidates, MergeLevel level, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_opt.BaseUrl) || string.IsNullOrWhiteSpace(_opt.Model))
            throw new ExtractionFailedException("The AI provider has no base URL or model configured.");

        var messages = new JsonArray
        {
            new JsonObject { ["role"] = "system", ["content"] = ConsolidationPrompt.SystemFor(level) },
            new JsonObject { ["role"] = "user", ["content"] = ConsolidationPrompt.BuildUserMessage(candidates) },
        };

        var (response, c, warnings) = await CompleteJsonAsync<ConsolidationResponse>(
            messages, ConsolidationPrompt.SchemaName, ConsolidationPrompt.Schema(), cancellationToken);
        logger.LogInformation("Consolidation call: {Candidates} candidates, {Groups} merge group(s), prompt {Prompt}, completion {Completion}",
            candidates.Count, response.Merges.Count, c.Usage?.PromptTokens, c.Usage?.CompletionTokens);
        return new ConsolidationResult(response, c.Model, c.FinishReason, c.Usage, warnings);
    }

    /// <summary>Completes, parses as <typeparamref name="T"/>, and retries once with a correction if the JSON is bad.</summary>
    private async Task<(T Value, Completion Completion, List<string> Warnings)> CompleteJsonAsync<T>(
        JsonArray messages, string schemaName, JsonObject schema, CancellationToken ct) where T : class
    {
        var warnings = new List<string>();
        var first = await CompleteAsync(messages, schemaName, schema, ct);
        if (TryParse<T>(first.Content, out var value, out var parseError))
            return (value!, first, warnings);

        // Validate-and-retry: many OpenAI-compatible backends only partially honour strict schemas.
        logger.LogWarning("Model returned invalid JSON ({Error}); retrying once.", parseError);
        warnings.Add($"First response was invalid JSON ({parseError}); retried.");
        messages.Add(new JsonObject { ["role"] = "assistant", ["content"] = first.Content });
        messages.Add(new JsonObject
        {
            ["role"] = "user",
            ["content"] = "That was not valid JSON in the required shape. Respond again with the JSON object only, no prose or code fences.",
        });

        var second = await CompleteAsync(messages, schemaName, schema, ct);
        if (TryParse<T>(second.Content, out value, out parseError))
            return (value!, second, warnings);

        throw new ExtractionFailedException($"Model returned invalid JSON twice: {parseError}");
    }

    private ExtractionResult Result(ExtractionResponse response, Completion c, int estimate, List<string> warnings)
    {
        logger.LogInformation(
            "Extraction call: model {Model}, finish {Finish}, prompt {Prompt} (est {Estimate}), completion {CompletionTokens}, {Count} action points",
            c.Model, c.FinishReason, c.Usage?.PromptTokens, estimate, c.Usage?.CompletionTokens, response.ActionPoints.Count);

        // Local backends can silently truncate over-long prompts.
        if (c.Usage?.PromptTokens is int actual && actual < estimate / 2)
        {
            warnings.Add($"Prompt tokens reported ({actual}) are far below the estimate ({estimate}); the server may have truncated the input. Check its context length setting.");
        }
        return new ExtractionResult(response, c.Model, c.FinishReason, c.Usage, warnings);
    }

    private sealed record Completion(string Content, string Model, string? FinishReason, TokenUsage? Usage);

    private async Task<Completion> CompleteAsync(JsonArray messages, string schemaName, JsonObject schema, CancellationToken ct)
    {
        var body = new JsonObject
        {
            ["model"] = _opt.Model,
            ["messages"] = messages.DeepClone(),
            ["temperature"] = 0,
            ["max_tokens"] = _opt.ReserveOutput,
            ["stream"] = false,
        };
        switch (_opt.ResponseFormat)
        {
            case ResponseFormatMode.JsonSchema:
                body["response_format"] = new JsonObject
                {
                    ["type"] = "json_schema",
                    ["json_schema"] = new JsonObject
                    {
                        ["name"] = schemaName,
                        ["strict"] = true,
                        ["schema"] = schema,
                    },
                };
                break;
            case ResponseFormatMode.JsonObject:
                body["response_format"] = new JsonObject { ["type"] = "json_object" };
                break;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, _opt.BaseUrl.TrimEnd('/') + "/chat/completions")
        {
            Content = JsonContent.Create(body),
        };
        if (!string.IsNullOrEmpty(_opt.ApiKey))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _opt.ApiKey);

        using var response = await http.SendAsync(request, ct);
        var raw = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new ExtractionFailedException($"Provider returned {(int)response.StatusCode}: {Truncate(raw, 500)}");

        JsonNode? root;
        try { root = JsonNode.Parse(raw); }
        catch (JsonException ex) { throw new ExtractionFailedException($"Provider response was not JSON: {Truncate(raw, 200)}", ex); }

        var choice = root?["choices"]?[0];
        var content = choice?["message"]?["content"]?.GetValue<string>()
                      ?? throw new ExtractionFailedException($"Provider response had no message content: {Truncate(raw, 300)}");
        var finish = choice?["finish_reason"]?.GetValue<string>();
        var model = root?["model"]?.GetValue<string>() ?? _opt.Model;

        TokenUsage? usage = null;
        if (root?["usage"] is JsonObject u)
        {
            usage = new TokenUsage(
                u["prompt_tokens"]?.GetValue<int>(),
                u["completion_tokens"]?.GetValue<int>(),
                u["total_tokens"]?.GetValue<int>());
        }

        if (finish == "length")
        {
            throw new ExtractionFailedException(
                $"Response was truncated (finish_reason=length, completion {usage?.CompletionTokens} tokens). " +
                "Increase the model's reserved output in AI settings or use a smaller document.");
        }

        return new Completion(content, model, finish, usage);
    }

    internal static bool TryParse<T>(string content, out T? response, out string? error) where T : class
    {
        response = null;
        error = null;
        var json = StripCodeFence(content);
        try
        {
            response = JsonSerializer.Deserialize<T>(json);
            if (response is null) { error = "empty response"; return false; }
            return true;
        }
        catch (JsonException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static string StripCodeFence(string s)
    {
        var t = s.Trim();
        if (!t.StartsWith("```")) return t;
        int firstNl = t.IndexOf('\n');
        int lastFence = t.LastIndexOf("```", StringComparison.Ordinal);
        return firstNl > 0 && lastFence > firstNl ? t[(firstNl + 1)..lastFence].Trim() : t;
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";
}
