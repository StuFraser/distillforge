using System.Text;
using System.Text.Json.Nodes;
using DistillForge.Core.Extraction;
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
        - due: the deadline as a SHORT tag of at most 4 words (e.g. "Friday", "16 Nov", "End of Q3"), or null if not stated.
          Drop qualifiers such as "ideally", "by the week of" or "sometime before".
        - due_detail: the deadline copied exactly as worded in the text (e.g. "ideally the week of the 16th of November"),
          only when it contains more than the short due tag; otherwise null
        - evidence: one or more items, each with
            - segment_id: the segment label exactly as shown, e.g. "S12"
            - quote: a short phrase (at most 25 words) copied VERBATIM, character for character, from that segment

        Respond with JSON only, in this shape:
        {"action_points":[{"id":"ap_1","title":"...","summary":"...","owner":null,"due":null,"due_detail":null,"evidence":[{"segment_id":"S1","quote":"..."}]}]}
        If there are no action points, respond with {"action_points":[]}.
        """;

    private const string PartialNotice =
        "This is part {0} of {1} of a longer document. Neighbouring parts overlap, so you only see some of the segments. " +
        "Report only action points supported by the segments below.\n\n";

    public static string BuildUserMessage(ExtractionWindow window)
    {
        var sb = new StringBuilder();
        if (window.Count > 1) sb.AppendFormat(PartialNotice, window.Index, window.Count);
        sb.Append("Source segments:\n\n");
        foreach (var s in window.Segments)
        {
            sb.Append('[').Append(s.Label).Append("] ").Append(s.Text).Append("\n\n");
        }
        return sb.ToString();
    }

    /// <summary>Prompt tokens that aren't segment text: system prompt, headings and the multi-part notice.</summary>
    public static int FixedOverhead { get; } = TokenEstimator.Estimate(System) + 20 + TokenEstimator.Estimate(PartialNotice) + 8;

    /// <summary>Rough prompt size: fixed overhead + segments + per-segment label/separator overhead.</summary>
    public static int EstimateTokens(IReadOnlyList<Segment> segments) =>
        FixedOverhead + segments.Sum(s => s.TokenEstimate + WindowPlanner.PerSegmentOverhead);

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
                    ["required"] = new JsonArray("id", "title", "summary", "owner", "due", "due_detail", "evidence"),
                    ["properties"] = new JsonObject
                    {
                        ["id"] = new JsonObject { ["type"] = "string" },
                        ["title"] = new JsonObject { ["type"] = "string" },
                        ["summary"] = new JsonObject { ["type"] = "string" },
                        ["owner"] = new JsonObject { ["type"] = new JsonArray("string", "null") },
                        ["due"] = new JsonObject { ["type"] = new JsonArray("string", "null") },
                        ["due_detail"] = new JsonObject { ["type"] = new JsonArray("string", "null") },
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
