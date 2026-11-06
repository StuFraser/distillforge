using System.Text;
using System.Text.Json.Nodes;
using DistillForge.Core.Extraction;
using DistillForge.Core.Text;

namespace DistillForge.Api.Ai;

/// <summary>Pass 2 prompt: find candidates that describe the same task. Grouping only; no rewriting.</summary>
public static class ConsolidationPrompt
{
    public const string SchemaName = "merges";

    public const string System = """
        You are reviewing action points that were extracted separately from overlapping parts of one document.
        The same task is sometimes reported more than once, usually with different wording.

        Each candidate looks like: c3 | title | owner | due | segments S10,S11 | summary

        Find groups of candidates that describe exactly the SAME task. Candidates that merely share a topic,
        a person or similar wording but are different tasks MUST stay separate. If unsure, do not merge.
        Do not rewrite or invent anything.

        For each group return the id to keep and the ids of the duplicates to fold into it.
        Respond with JSON only, in this shape:
        {"merges":[{"keep":"c3","duplicates":["c7","c9"]}]}
        If nothing should be merged, respond with {"merges":[]}.
        """;

    public static string BuildUserMessage(IReadOnlyList<ConsolidationCandidate> candidates)
    {
        var sb = new StringBuilder("Candidates:\n\n");
        foreach (var c in candidates)
        {
            sb.Append(c.Id).Append(" | ").Append(c.Title)
              .Append(" | ").Append(c.Owner ?? "-")
              .Append(" | ").Append(c.Due ?? "-")
              .Append(" | ").Append(string.Join(',', c.SegmentLabels));
            if (!string.IsNullOrWhiteSpace(c.Summary)) sb.Append(" | ").Append(c.Summary);
            sb.Append('\n');
        }
        return sb.ToString();
    }

    public static int EstimateTokens(IReadOnlyList<ConsolidationCandidate> candidates) =>
        TokenEstimator.Estimate(System) + 20 + TokenEstimator.Estimate(BuildUserMessage(candidates));

    public static JsonObject Schema() => new()
    {
        ["type"] = "object",
        ["additionalProperties"] = false,
        ["required"] = new JsonArray("merges"),
        ["properties"] = new JsonObject
        {
            ["merges"] = new JsonObject
            {
                ["type"] = "array",
                ["items"] = new JsonObject
                {
                    ["type"] = "object",
                    ["additionalProperties"] = false,
                    ["required"] = new JsonArray("keep", "duplicates"),
                    ["properties"] = new JsonObject
                    {
                        ["keep"] = new JsonObject { ["type"] = "string" },
                        ["duplicates"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" } },
                    },
                },
            },
        },
    };
}
