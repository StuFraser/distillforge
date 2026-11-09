using DistillForge.Api.Services;
using DistillForge.Core.Contracts;
using DistillForge.Data;
using DistillForge.Data.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.EntityFrameworkCore;

namespace DistillForge.Api.Functions;

public sealed class CategoryFunctions(DistillForgeDbContext db)
{
    private const string ArchivedMessage = "This workspace is archived; unarchive it to change categories.";

    [Function("ListCategories")]
    public async Task<IActionResult> List(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "workspaces/{id:guid}/categories")] HttpRequest req,
        Guid id, CancellationToken ct)
    {
        var rows = await db.Categories.AsNoTracking()
            .Where(c => c.WorkspaceId == id)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .ToListAsync(ct);
        return new OkObjectResult(rows.Select(c => c.ToDto()));
    }

    [Function("CreateCategory")]
    public async Task<IActionResult> Create(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "workspaces/{id:guid}/categories")] HttpRequest req,
        Guid id, CancellationToken ct)
    {
        var body = await WorkspaceFunctions.ReadBody<CategoryRequest>(req, ct);
        if (body is null) return new BadRequestObjectResult("Expected JSON body with name and colour.");
        if (CategoryValidation.Validate(body) is { } error) return new BadRequestObjectResult(error);

        var w = await db.Workspaces.Include(x => x.Categories).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (w is null) return new NotFoundResult();
        if (w.IsArchived) return new ConflictObjectResult(ArchivedMessage);
        if (CategoryValidation.NameTaken(body.Name, w.Categories.Select(c => c.Name)))
            return new ConflictObjectResult($"A category named '{body.Name.Trim()}' already exists.");

        var category = new Category
        {
            Id = Guid.NewGuid(),
            WorkspaceId = id,
            Name = body.Name.Trim(),
            Colour = body.Colour,
            SortOrder = w.Categories.Count == 0 ? 0 : w.Categories.Max(c => c.SortOrder) + 1,
        };
        db.Categories.Add(category);
        w.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return new CreatedResult($"/api/categories/{category.Id}", category.ToDto());
    }

    [Function("UpdateCategory")]
    public async Task<IActionResult> Update(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "categories/{id:guid}")] HttpRequest req,
        Guid id, CancellationToken ct)
    {
        var body = await WorkspaceFunctions.ReadBody<CategoryRequest>(req, ct);
        if (body is null) return new BadRequestObjectResult("Expected JSON body with name and colour.");
        if (CategoryValidation.Validate(body) is { } error) return new BadRequestObjectResult(error);

        var category = await db.Categories.Include(c => c.Workspace).ThenInclude(w => w!.Categories)
            .FirstOrDefaultAsync(c => c.Id == id, ct);
        if (category is null) return new NotFoundResult();
        var w = category.Workspace!;
        if (w.IsArchived) return new ConflictObjectResult(ArchivedMessage);
        if (CategoryValidation.NameTaken(body.Name, w.Categories.Where(c => c.Id != id).Select(c => c.Name)))
            return new ConflictObjectResult($"A category named '{body.Name.Trim()}' already exists.");

        category.Name = body.Name.Trim();
        category.Colour = body.Colour;
        w.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return new OkObjectResult(category.ToDto());
    }

    [Function("DeleteCategory")]
    public async Task<IActionResult> Delete(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "categories/{id:guid}")] HttpRequest req,
        Guid id, CancellationToken ct)
    {
        var category = await db.Categories.Include(c => c.Workspace).FirstOrDefaultAsync(c => c.Id == id, ct);
        if (category is null) return new NotFoundResult();
        if (category.Workspace!.IsArchived) return new ConflictObjectResult(ArchivedMessage);

        // Notes in the category fall back to Uncategorised (FK is SET NULL).
        db.Categories.Remove(category);
        category.Workspace.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return new NoContentResult();
    }

    [Function("MoveActionItem")]
    public async Task<IActionResult> Move(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "actions/{id:guid}/category")] HttpRequest req,
        Guid id, CancellationToken ct)
    {
        var body = await WorkspaceFunctions.ReadBody<MoveActionItemRequest>(req, ct);
        if (body is null) return new BadRequestObjectResult("Expected JSON body with categoryId (or null).");

        var item = await db.ActionItems.Include(a => a.Workspace)
            .Include(a => a.Evidence).ThenInclude(e => e.Segment)
            .FirstOrDefaultAsync(a => a.Id == id, ct);
        if (item is null) return new NotFoundResult();
        if (item.Workspace!.IsArchived) return new ConflictObjectResult("This workspace is archived; unarchive it to move notes.");

        if (body.CategoryId is Guid cid &&
            !await db.Categories.AnyAsync(c => c.Id == cid && c.WorkspaceId == item.WorkspaceId, ct))
            return new BadRequestObjectResult("That category doesn't belong to this note's workspace.");

        if (item.MergedIntoId is not null)
            return new BadRequestObjectResult("This note is merged into another and follows it; move the other note instead.");

        item.CategoryId = body.CategoryId;
        // Merged notes follow their parent.
        foreach (var child in await db.ActionItems.Where(a => a.MergedIntoId == id).ToListAsync(ct))
            child.CategoryId = body.CategoryId;
        item.Workspace.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        var dtos = await ActionItemQueries.LoadDtosAsync(db, item.WorkspaceId, ct);
        return new OkObjectResult(dtos.First(d => d.Id == id));
    }
}
