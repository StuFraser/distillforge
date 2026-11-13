using System.Text;
using System.Text.Json.Nodes;
using DistillForge.Core.Contracts;
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

        Separately, list in "possible" any candidates that MIGHT be the same underlying task but that you are
        not sure enough about to merge, so a person can review them. Typical cases: something requested or agreed
        early on and then confirmed, restated or followed up later; or the same task described in different words.
        Never put an id in both "merges" and "possible", and do not list tasks that are merely on the same topic.

        Respond with JSON only, in this shape:
        {"merges":[{"keep":"c3","duplicates":["c7","c9"]}],"possible":[{"ids":["c2","c11"]}]}
        Use an empty list for either key when nothing applies.
        """;

    /// <summary>Low: the model must not merge, only flag. Aggressive: also merge the same outcome worded differently.</summary>
    public static string SystemFor(MergeLevel level) => level switch
    {
        MergeLevel.Low => System + """


            IMPORTANT: do NOT merge anything. Leave "merges" empty. Put every group of candidates you think are the same task
            in "possible", so a person can decide.
            """,
        MergeLevel.Aggressive => System + """


            Be generous when merging. Treat candidates as the same task when they ask for the same outcome from the same
            person or team, even if the wording differs, the task is split into steps, or it is mentioned once in discussion and
            again in a recap or summary. Different people, or different deliverables, stay separate. When unsure between
            "merges" and "possible", prefer "merges".
            """,
        _ => System,
    };

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
        ["required"] = new JsonArray("merges", "possible"),
        ["properties"] = new JsonObject
        {
            ["possible"] = new JsonObject
            {
                ["type"] = "array",
                ["items"] = new JsonObject
                {
                    ["type"] = "object",
                    ["additionalProperties"] = false,
                    ["required"] = new JsonArray("ids"),
                    ["properties"] = new JsonObject
                    {
                        ["ids"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" } },
                    },
                },
            },
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
