using System.Text;
using System.Text.Json;
using DistillForge.Core.Contracts;
using DistillForge.Core.Segmentation;
using DistillForge.Data;
using DistillForge.Data.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DistillForge.Api.Tests;

/// <summary>
/// A throwaway in-memory SQLite database with the real model. Foreign keys are on, so cascade and SET NULL
/// delete behaviour is exercised for real (the EF InMemory provider would silently ignore it).
/// </summary>
public sealed class TestDb : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:;Foreign Keys=True");

    public TestDb()
    {
        _connection.Open();
        using var db = NewContext();
        db.Database.EnsureCreated();
    }

    /// <summary>A fresh context on the shared connection, like a new request would get (so nothing is served from the change tracker).</summary>
    public DistillForgeDbContext NewContext() =>
        new(new DbContextOptionsBuilder<DistillForgeDbContext>().UseSqlite(_connection).Options);

    /// <summary>
    /// A workspace whose source is "AAAAAAAAAA\n\nBBBBBBBBBB\n\nCCCCCCCCCC" split into three 10-character segments
    /// (S1 0-10, S2 12-22, S3 24-34), so selections across segment boundaries are easy to reason about.
    /// </summary>
    public Workspace SeedWorkspace(bool archived = false)
    {
        using var db = NewContext();
        var w = new Workspace
        {
            Id = Guid.NewGuid(),
            Title = "Test",
            SourceText = "AAAAAAAAAA\n\nBBBBBBBBBB\n\nCCCCCCCCCC",
            Strategy = SegmentationStrategyKind.Prose,
            IsArchived = archived,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
            UpdatedAt = DateTimeOffset.UtcNow.AddDays(-1),
        };
        db.Workspaces.Add(w);
        foreach (var (i, start) in new[] { 0, 12, 24 }.Select((s, i) => (i, s)))
            db.Segments.Add(new DistillForge.Data.Entities.Segment { Id = Guid.NewGuid(), WorkspaceId = w.Id, Ordinal = i, Label = $"S{i + 1}", Start = start, End = start + 10, TokenEstimate = 3 });
        db.SaveChanges();
        return w;
    }

    public Category SeedCategory(Guid workspaceId, string name = "Cat")
    {
        using var db = NewContext();
        var c = new Category { Id = Guid.NewGuid(), WorkspaceId = workspaceId, Name = name };
        db.Categories.Add(c);
        db.SaveChanges();
        return c;
    }

    public ActionItem SeedNote(Guid workspaceId, string title = "Note", int sortOrder = 0, Guid? categoryId = null,
        Guid? mergedIntoId = null, bool manual = false, Guid? evidenceSegmentId = null)
    {
        using var db = NewContext();
        var n = new ActionItem
        {
            Id = Guid.NewGuid(), WorkspaceId = workspaceId, Title = title, SortOrder = sortOrder, CategoryId = categoryId,
            MergedIntoId = mergedIntoId, IsManual = manual, CreatedAt = DateTimeOffset.UtcNow,
        };
        if (evidenceSegmentId is Guid seg)
            n.Evidence.Add(new Evidence { Id = Guid.NewGuid(), SegmentId = seg, Quote = "AAAA", Start = 0, End = 4, MatchQuality = DistillForge.Core.Extraction.MatchQuality.Exact });
        db.ActionItems.Add(n);
        db.SaveChanges();
        return n;
    }

    public void SeedLink(Guid workspaceId, Guid a, Guid b)
    {
        using var db = NewContext();
        db.ActionItemLinks.Add(new ActionItemLink { Id = Guid.NewGuid(), WorkspaceId = workspaceId, ActionItemAId = a, ActionItemBId = b, Source = DuplicateSource.Similarity, CreatedAt = DateTimeOffset.UtcNow });
        db.SaveChanges();
    }

    public void Dispose() => _connection.Dispose();
}

public static class Http
{
    public static HttpRequest Json(object? body)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.ContentType = "application/json";
        ctx.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(body)));
        return ctx.Request;
    }

    /// <summary>A request with no body or content type, as a bodyless POST arrives.</summary>
    public static HttpRequest Empty() => new DefaultHttpContext().Request;

    public static T Ok<T>(IActionResult result) where T : class =>
        Assert.IsType<T>(Assert.IsType<OkObjectResult>(result).Value);
}
