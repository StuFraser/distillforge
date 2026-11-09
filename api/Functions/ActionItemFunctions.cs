using DistillForge.Api.Services;
using DistillForge.Core.Contracts;
using DistillForge.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.EntityFrameworkCore;

namespace DistillForge.Api.Functions;

public sealed class ActionItemFunctions(DistillForgeDbContext db)
{
    [Function("UpdateActionItem")]
    public async Task<IActionResult> Update(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "actions/{id:guid}")] HttpRequest req,
        Guid id,
        CancellationToken ct)
    {
        var body = await WorkspaceFunctions.ReadBody<UpdateActionItemRequest>(req, ct);
        if (body is null) return new BadRequestObjectResult("Expected JSON body with title, summary, owner, due and colour.");
        if (ActionItemValidation.Validate(body) is { } error) return new BadRequestObjectResult(error);

        var item = await db.ActionItems
            .Include(a => a.Workspace)
            .Include(a => a.Evidence).ThenInclude(e => e.Segment)
            .FirstOrDefaultAsync(a => a.Id == id, ct);
        if (item is null) return new NotFoundResult();
        if (item.Workspace!.IsArchived) return new ConflictObjectResult("This workspace is archived; unarchive it to edit notes.");

        item.Title = body.Title.Trim();
        item.Summary = Blank(body.Summary);
        item.Owner = Blank(body.Owner);
        item.Due = Blank(body.Due);
        item.Colour = body.Colour;
        item.Workspace.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        var dtos = await ActionItemQueries.LoadDtosAsync(db, item.WorkspaceId, ct);
        return new OkObjectResult(dtos.First(d => d.Id == id));
    }

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
