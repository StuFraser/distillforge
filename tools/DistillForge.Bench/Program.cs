using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DistillForge.Api.Ai;
using DistillForge.Api.Services;
using DistillForge.Bench;
using DistillForge.Core.Extraction;
using DistillForge.Core.Segmentation;
using DistillForge.Core.Text;
using Microsoft.Extensions.Logging.Abstractions;

// Benchmark harness: runs the production extraction pipeline (no database, queue or UI) over the sample documents
// for every model x window-profile combination and reports what came out. See tools/DistillForge.Bench/README.md.

var opts = Options.Parse(args);
if (opts.Help) { Options.PrintHelp(); return 0; }
if (opts.Error is not null) { Console.Error.WriteLine(opts.Error); Options.PrintHelp(); return 2; }

BenchConfig config;
try { config = BenchConfig.Load(opts.ConfigPath); }
catch (Exception ex) { Console.Error.WriteLine($"Could not load {opts.ConfigPath}: {ex.Message}"); return 2; }

var files = Directory.EnumerateFiles(opts.SamplesDir)
    .Where(f => new[] { ".txt", ".md", ".docx", ".pdf" }.Contains(Path.GetExtension(f).ToLowerInvariant()))
    .Where(f => !Path.GetFileName(f).Equals("README.md", StringComparison.OrdinalIgnoreCase))
    .Where(f => opts.Docs.Count == 0 || opts.Docs.Any(d => Path.GetFileName(f).StartsWith(d, StringComparison.OrdinalIgnoreCase)))
    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
    .ToList();
if (opts.MaxDocs > 0) files = files.Take(opts.MaxDocs).ToList();
if (files.Count == 0) { Console.Error.WriteLine($"No matching documents in {opts.SamplesDir}."); return 2; }

var models = config.Models.Where(m => opts.Models.Count == 0 || opts.Models.Contains(m.Name, StringComparer.OrdinalIgnoreCase)).ToList();
var profiles = config.Profiles.Where(p => opts.Profiles.Count == 0 || opts.Profiles.Contains(p.Name, StringComparer.OrdinalIgnoreCase)).ToList();
if (models.Count == 0 || profiles.Count == 0) { Console.Error.WriteLine("No model/profile matches the filters."); return 2; }
int repeats = opts.Runs ?? config.RunsPerCell;

foreach (var m in models.Where(m => m.ResolveKey() is null && m.ApiKeyEnv is not null))
    Console.Error.WriteLine($"warning: model '{m.Name}' expects env var {m.ApiKeyEnv}, which is not set.");

// Load each document once: same path the API uses on upload and ingest.
var extractorFiles = new SourceFileExtractor();
var docs = new List<(string Name, IReadOnlyList<Segment> Segments, string Source)>();
foreach (var f in files)
{
    try
    {
        await using var fs = File.OpenRead(f);
        var text = SourceNormaliser.Normalise(await extractorFiles.ExtractAsync(fs, f, CancellationToken.None));
        var segments = new Segmenter().Split(text, StrategyDetector.Detect(text));
        docs.Add((Path.GetFileName(f), segments, text));
    }
    catch (Exception ex) { Console.Error.WriteLine($"skipping {Path.GetFileName(f)}: {ex.Message}"); }
}

int total = docs.Count * models.Count * profiles.Count * repeats;
Console.WriteLine($"{docs.Count} document(s) x {models.Count} model(s) x {profiles.Count} profile(s) x {repeats} run(s) = {total} run(s)");

var report = new BenchReport { StartedAt = DateTimeOffset.UtcNow, SamplesDir = Path.GetFullPath(opts.SamplesDir) };
using var stopAll = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; stopAll.Cancel(); Console.Error.WriteLine("\nStopping after the current call; writing partial results..."); };

int n = 0;
try
{
    foreach (var model in models)
    foreach (var profile in profiles)
    {
        var provider = new AiProviderOptions
        {
            BaseUrl = model.BaseUrl,
            Model = model.Model,
            ApiKey = model.ResolveKey(),
            ContextTokens = profile.ContextTokens,
            ReserveOutput = profile.ReserveOutput,
            ResponseFormat = model.ResponseFormat,
            TimeoutSeconds = model.TimeoutSeconds,
        };
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(model.TimeoutSeconds) };
        var extractor = new OpenAiCompatibleActionExtractor(http, provider, NullLogger<OpenAiCompatibleActionExtractor>.Instance);

        foreach (var doc in docs)
        for (int r = 1; r <= repeats; r++)
        {
            stopAll.Token.ThrowIfCancellationRequested();
            Console.Write($"[{++n}/{total}] {doc.Name} · {model.Name}/{profile.Name}" + (repeats > 1 ? $" #{r}" : "") + " ... ");
            var result = await RunOneAsync(doc.Name, doc.Segments, model.Name, profile.Name, r, extractor, config.RunTimeoutMinutes, stopAll.Token);
            report.Runs.Add(result);
            Console.WriteLine(result.Succeeded
                ? $"{result.Notes} notes, {result.Windows} window(s), {result.EvidenceSegment}/{result.EvidenceTotal} fallback quotes, {result.Seconds:F0}s"
                : $"FAILED ({result.Error})");
        }
    }
}
catch (OperationCanceledException) { }

Directory.CreateDirectory(opts.OutDir);
var stamp = report.StartedAt.ToLocalTime().ToString("yyyyMMdd-HHmmss");
var jsonPath = Path.Combine(opts.OutDir, $"bench-{stamp}.json");
var csvPath = Path.Combine(opts.OutDir, $"bench-{stamp}.csv");
File.WriteAllText(jsonPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true, Converters = { new JsonStringEnumConverter() } }));
File.WriteAllText(csvPath, Reporting.ToCsv(report));

Reporting.PrintSummary(report, opts.BaselinePath);
Console.WriteLine($"\nWrote {jsonPath}\n      {csvPath}");
return report.Runs.Any(r => !r.Succeeded) ? 1 : 0;

static async Task<RunResult> RunOneAsync(string doc, IReadOnlyList<Segment> segments, string model, string profile, int repeat,
    IActionExtractor extractor, int timeoutMinutes, CancellationToken stop)
{
    var res = new RunResult { Document = doc, Model = model, Profile = profile, Repeat = repeat, Segments = segments.Count };
    var sw = Stopwatch.StartNew();
    using var cts = CancellationTokenSource.CreateLinkedTokenSource(stop);
    cts.CancelAfter(TimeSpan.FromMinutes(timeoutMinutes));
    try
    {
        var r = await ExtractionPipeline.RunAsync(segments, extractor, null, NullLogger.Instance, cts.Token);
        var s = r.Stats;
        res.Succeeded = true;
        res.Windows = s.Windows; res.FailedWindows = s.FailedWindows; res.WindowBudgetTokens = s.WindowBudgetTokens;
        res.FoundBeforeMerge = s.FoundBeforeMerge; res.AfterWindowMerge = s.AfterWindowMerge;
        res.ConsolidationRan = s.ConsolidationRan; res.ConsolidationSkipped = s.ConsolidationSkipped;
        res.ConsolidationFailed = s.ConsolidationFailed; res.ConsolidationMerged = s.ConsolidationMerged;
        res.Notes = r.ActionPoints.Count;
        res.WithOwner = r.ActionPoints.Count(a => !string.IsNullOrWhiteSpace(a.Owner));
        res.WithDue = r.ActionPoints.Count(a => !string.IsNullOrWhiteSpace(a.Due));
        foreach (var e in r.ActionPoints.SelectMany(a => a.Evidence))
        {
            if (e.Quality == MatchQuality.Exact) res.EvidenceExact++;
            else if (e.Quality == MatchQuality.Normalised) res.EvidenceNormalised++;
            else res.EvidenceSegment++;
        }
        res.Warnings = r.Warnings.Count; res.WarningList = r.Warnings.ToList();
        res.PromptTokens = r.PromptTokens; res.CompletionTokens = r.CompletionTokens;
        res.NoteList = r.ActionPoints.Select(a => new NoteSummary
        {
            Title = a.Title, Owner = a.Owner, Due = a.Due,
            Quote = a.Evidence[0].Quote, Quality = a.Evidence[0].Quality.ToString(),
        }).ToList();
    }
    catch (OperationCanceledException) when (stop.IsCancellationRequested) { throw; }
    catch (OperationCanceledException) { res.Error = $"run timed out after {timeoutMinutes} min"; }
    catch (Exception ex) { res.Error = ex is ExtractionFailedException or HttpRequestException ? ex.Message : $"{ex.GetType().Name}: {ex.Message}"; }
    res.Seconds = sw.Elapsed.TotalSeconds;
    return res;
}

sealed class Options
{
    public string ConfigPath = "bench.json";
    public string SamplesDir = FindSamples();
    public string OutDir = "bench-results";
    public string? BaselinePath;
    public List<string> Docs = [], Models = [], Profiles = [];
    public int MaxDocs;
    public int? Runs;
    public bool Help;
    public string? Error;

    static string FindSamples()
    {
        for (var d = new DirectoryInfo(Directory.GetCurrentDirectory()); d is not null; d = d.Parent)
            if (Directory.Exists(Path.Combine(d.FullName, "samples"))) return Path.Combine(d.FullName, "samples");
        return "samples";
    }

    static List<string> Split(string v) => v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    public static Options Parse(string[] a)
    {
        var o = new Options();
        for (int i = 0; i < a.Length; i++)
        {
            string Next() => i + 1 < a.Length ? a[++i] : throw new ArgumentException($"{a[i]} needs a value");
            try
            {
                switch (a[i])
                {
                    case "-h": case "--help": o.Help = true; break;
                    case "--config": o.ConfigPath = Next(); break;
                    case "--samples": o.SamplesDir = Next(); break;
                    case "--out": o.OutDir = Next(); break;
                    case "--baseline": o.BaselinePath = Next(); break;
                    case "--docs": o.Docs = Split(Next()); break;
                    case "--models": o.Models = Split(Next()); break;
                    case "--profiles": o.Profiles = Split(Next()); break;
                    case "--max-docs": o.MaxDocs = int.Parse(Next()); break;
                    case "--runs": o.Runs = int.Parse(Next()); break;
                    default: o.Error = $"Unknown option {a[i]}"; return o;
                }
            }
            catch (Exception ex) { o.Error = ex.Message; return o; }
        }
        return o;
    }

    public static void PrintHelp() => Console.WriteLine("""
        DistillForge benchmark harness

        dotnet run --project tools/DistillForge.Bench -- [options]

          --config <file>     models/profiles JSON (default bench.json; copy bench.sample.json)
          --samples <dir>     documents to run (default: nearest samples/ folder)
          --docs <a,b>        only files whose name starts with one of these, e.g. 01,04
          --models <a,b>      only these model names from the config
          --profiles <a,b>    only these profile names from the config
          --max-docs <n>      only run the first n documents, in filename order (cheap smoke test)
          --runs <n>          repeats per cell (overrides the config; use 2-3 to see variance)
          --out <dir>         result folder (default bench-results)
          --baseline <file>   earlier JSON report to compare against
        """);
}
