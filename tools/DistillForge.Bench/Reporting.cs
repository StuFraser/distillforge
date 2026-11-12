using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DistillForge.Bench;

public static class Reporting
{
    public static string ToCsv(BenchReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("document,model,profile,repeat,succeeded,seconds,segments,windows,failedWindows,foundBeforeMerge,afterWindowMerge,consolidationRan,consolidationSkipped,consolidationMerged,notes,withOwner,withDue,evidenceExact,evidenceNormalised,evidenceSegment,warnings,promptTokens,completionTokens,error");
        foreach (var r in report.Runs)
            sb.AppendLine(string.Join(',',
                Q(r.Document), Q(r.Model), Q(r.Profile), r.Repeat, r.Succeeded, r.Seconds.ToString("F1", CultureInfo.InvariantCulture),
                r.Segments, r.Windows, r.FailedWindows, r.FoundBeforeMerge, r.AfterWindowMerge, r.ConsolidationRan, r.ConsolidationSkipped, r.ConsolidationMerged,
                r.Notes, r.WithOwner, r.WithDue, r.EvidenceExact, r.EvidenceNormalised, r.EvidenceSegment, r.Warnings, r.PromptTokens, r.CompletionTokens, Q(r.Error)));
        return sb.ToString();

        static string Q(string? s) => s is null ? "" : "\"" + s.Replace("\"", "\"\"").Replace("\n", " ") + "\"";
    }

    public static void PrintSummary(BenchReport report, string? baselinePath)
    {
        BenchReport? baseline = null;
        if (baselinePath is not null)
        {
            try
            {
                baseline = JsonSerializer.Deserialize<BenchReport>(File.ReadAllText(baselinePath),
                    new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() }, PropertyNameCaseInsensitive = true });
            }
            catch (Exception ex) { Console.Error.WriteLine($"Could not read baseline {baselinePath}: {ex.Message}"); }
        }

        // Per document x cell: median-free simple mean across repeats.
        var rows = report.Runs
            .GroupBy(r => (r.Document, r.Cell))
            .Select(g => new
            {
                g.Key.Document, g.Key.Cell,
                Ok = g.Count(r => r.Succeeded), Total = g.Count(),
                Notes = g.Where(r => r.Succeeded).Select(r => r.Notes).ToList(),
                Windows = g.First().Windows,
                Fallback = g.Where(r => r.Succeeded).Sum(r => r.EvidenceSegment),
                Evidence = g.Where(r => r.Succeeded).Sum(r => r.EvidenceTotal),
                Seconds = g.Average(r => r.Seconds),
                Skipped = g.Any(r => r.ConsolidationSkipped),
                Failed = g.Max(r => r.FailedWindows),
            })
            .OrderBy(r => r.Document).ThenBy(r => r.Cell)
            .ToList();

        Console.WriteLine();
        Console.WriteLine("{0,-36} {1,-24} {2,9} {3,4} {4,10} {5,6}  {6}", "Document", "Model/profile", "Notes", "Win", "Fallback", "Secs", baseline is null ? "Flags" : "Flags / vs baseline");
        Console.WriteLine(new string('-', 110));
        foreach (var r in rows)
        {
            string notes = r.Notes.Count == 0 ? "-" : r.Notes.Count == 1 ? r.Notes[0].ToString() : $"{r.Notes.Average():F1}±{r.Notes.Max() - r.Notes.Min()}";
            var flags = new List<string>();
            if (r.Ok < r.Total) flags.Add($"{r.Total - r.Ok} failed");
            if (r.Failed > 0) flags.Add($"{r.Failed} window(s) failed");
            if (r.Skipped) flags.Add("pass 2 skipped");
            if (baseline is not null)
            {
                var prev = baseline.Runs.Where(b => b.Document == r.Document && b.Cell == r.Cell && b.Succeeded).Select(b => b.Notes).ToList();
                if (prev.Count > 0 && r.Notes.Count > 0) flags.Add($"{r.Notes.Average() - prev.Average():+0.#;-0.#;0} notes");
            }
            Console.WriteLine("{0,-36} {1,-24} {2,9} {3,4} {4,10} {5,6:F0}  {6}",
                Trunc(r.Document, 36), Trunc(r.Cell, 24), notes, r.Windows, r.Evidence == 0 ? "-" : $"{r.Fallback}/{r.Evidence}", r.Seconds, string.Join(", ", flags));
        }

        var byCell = report.Runs.Where(r => r.Succeeded).GroupBy(r => r.Cell).ToList();
        if (byCell.Count > 1)
        {
            Console.WriteLine("\nPer model/profile across all documents:");
            foreach (var g in byCell.OrderBy(g => g.Key))
            {
                int ev = g.Sum(r => r.EvidenceTotal);
                Console.WriteLine("  {0,-24} {1,4} notes over {2} run(s); fallback quotes {3}; failed windows {4}; avg {5:F0}s/run",
                    g.Key, g.Sum(r => r.Notes), g.Count(), ev == 0 ? "-" : $"{100.0 * g.Sum(r => r.EvidenceSegment) / ev:F0}%", g.Sum(r => r.FailedWindows), g.Average(r => r.Seconds));
            }
        }
    }

    private static string Trunc(string s, int n) => s.Length <= n ? s : s[..(n - 1)] + "…";
}
