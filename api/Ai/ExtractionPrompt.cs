using System.Text;
using System.Text.Json.Nodes;
using DistillForge.Core.Segmentation;
using DistillForge.Core.Text;

namespace DistillForge.Api.Ai;

public static class ExtractionPrompt
{
    public const string System = """
        You extract action points from a source document for a business analyst.

        An action point is a task, commitment, follow-up, request or decision that requires someone to do something.
        Do not report general discussion, background or opinions. Do not invent anything that is not in the text.

        The document is split into numbered segments like "[S12] text". For each action point return:
        - id: "ap_1", "ap_2", ...
        - title: short imperative phrase, at most 10 words
        - summary: one or two sentences of context
        - owner: the person or team responsible, or null if not stated
        - due: the deadline exactly as stated (e.g. "Friday", "end of Q3"), or null if not stated
        - evidence: one or more items, each with
            - segment_id: the segment label exactly as shown, e.g. "S12"
            - quote: a short phrase (at most 25 words) copied VERBATIM, character for character, from that segment

        Respond with JSON only, in this shape:
        {"action_points":[{"id":"ap_1","title":"...","summary":"...","owner":null,"due":null,"evidence":[{"segment_id":"S1","quote":"..."}]}]}
        If there are no action points, respond with {"action_points":[]}.
        """;

    public static string BuildUserMessage(IReadOnlyList<Segment> segments)
    {
        var sb = new StringBuilder("Source segments:\n\n");
        foreach (var s in segments)
        {
            sb.Append('[').Append(s.Label).Append("] ").Append(s.Text).Append("\n\n");
        }
        return sb.ToString();
    }

    /// <summary>Rough prompt size: system + segments + ~8 tokens of label/separator overhead per segment.</summary>
    public static int EstimateTokens(IReadOnlyList<Segment> segments) =>
        TokenEstimator.Estimate(System) + 20 + segments.Sum(s => s.TokenEstimate + 8);

    /// <summary>JSON schema for response_format: json_schema. Strict mode requires every property listed as required.</summary>
    public static JsonObject Schema() => new()
    {
        ["type"] = "object",
        ["additionalProperties"] = false,
        ["required"] = new JsonArray("action_points"),
        ["properties"] = new JsonObject
        {
            ["action_points"] = new JsonObject
            {
                ["type"] = "array",
                ["items"] = new JsonObject
                {
                    ["type"] = "object",
                    ["additionalProperties"] = false,
                    ["required"] = new JsonArray("id", "title", "summary", "owner", "due", "evidence"),
                    ["properties"] = new JsonObject
                    {
                        ["id"] = new JsonObject { ["type"] = "string" },
                        ["title"] = new JsonObject { ["type"] = "string" },
                        ["summary"] = new JsonObject { ["type"] = "string" },
                        ["owner"] = new JsonObject { ["type"] = new JsonArray("string", "null") },
                        ["due"] = new JsonObject { ["type"] = new JsonArray("string", "null") },
                        ["evidence"] = new JsonObject
                        {
                            ["type"] = "array",
                            ["items"] = new JsonObject
                            {
                                ["type"] = "object",
                                ["additionalProperties"] = false,
                                ["required"] = new JsonArray("segment_id", "quote"),
                                ["properties"] = new JsonObject
                                {
                                    ["segment_id"] = new JsonObject { ["type"] = "string" },
                                    ["quote"] = new JsonObject { ["type"] = "string" },
                                },
                            },
                        },
                    },
                },
            },
        },
    };
}
