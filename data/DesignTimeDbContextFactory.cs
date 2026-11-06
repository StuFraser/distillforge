using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DistillForge.Data;

/// <summary>
/// Used by `dotnet ef` only (the Functions isolated host can't be booted by the EF tools).
/// Connection string lookup, first match wins:
///   1. ConnectionStrings__Postgres environment variable (CI / deployment pipelines)
///   2. Values.ConnectionStrings__Postgres in api/local.settings.json (local dev; same file `func start` uses)
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<DistillForgeDbContext>
{
    private const string Key = "ConnectionStrings__Postgres";

    public DistillForgeDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable(Key)
                 ?? ReadFromLocalSettings()
                 ?? throw new InvalidOperationException(
                     $"No Postgres connection string found. Set {Key} in api/local.settings.json " +
                     $"(copy api/local.settings.sample.json) or as an environment variable.");

        var options = new DbContextOptionsBuilder<DistillForgeDbContext>().UseNpgsql(cs).Options;
        return new DistillForgeDbContext(options);
    }

    private static string? ReadFromLocalSettings()
    {
        var path = FindLocalSettings();
        if (path is null) return null;

        using var doc = JsonDocument.Parse(File.ReadAllText(path),
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        return doc.RootElement.TryGetProperty("Values", out var values)
               && values.TryGetProperty(Key, out var value)
               && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    /// <summary>Walks up from the working directory and the tool's base directory looking for api/local.settings.json.</summary>
    private static string? FindLocalSettings()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
            {
                foreach (var candidate in new[] { Path.Combine(dir.FullName, "api", "local.settings.json"),
                                                  Path.Combine(dir.FullName, "local.settings.json") })
                {
                    if (File.Exists(candidate)) return candidate;
                }
            }
        }
        return null;
    }
}
