using DistillForge.Core.Codes;
using DistillForge.Data;
using Microsoft.EntityFrameworkCore;

namespace DistillForge.Api.Services;

/// <summary>
/// Hands out short codes that no row uses yet. Uniqueness is global (not per user), so the check ignores the per-user query
/// filter; the unique index is the backstop if two requests ever pick the same code at once.
/// </summary>
public static class CodeAllocator
{
    public static async Task<string> NewWorkspaceCodeAsync(DistillForgeDbContext db, CancellationToken ct)
    {
        while (true)
        {
            var code = ShortCode.New();
            if (!await db.Workspaces.IgnoreQueryFilters().AnyAsync(w => w.Code == code, ct)) return code;
        }
    }

    public static async Task<Queue<string>> NewNoteCodesAsync(DistillForgeDbContext db, int count, CancellationToken ct)
    {
        var chosen = new HashSet<string>();
        while (chosen.Count < count)
        {
            var batch = Enumerable.Range(0, count - chosen.Count).Select(_ => ShortCode.New()).Where(c => !chosen.Contains(c)).Distinct().ToList();
            var taken = await db.ActionItems.IgnoreQueryFilters().Where(a => batch.Contains(a.Code)).Select(a => a.Code).ToListAsync(ct);
            foreach (var c in batch) if (!taken.Contains(c)) chosen.Add(c);
        }
        return new Queue<string>(chosen);
    }
}
