using DistillForge.Data;
using DistillForge.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DistillForge.Api.Auth;

/// <summary>Maps a verified identity to its <see cref="AppUser"/> row, creating it on first sign-in.</summary>
public sealed class UserProvisioner(DistillForgeDbContext db, IOptions<AuthOptions> options)
{
    private static readonly TimeSpan SeenInterval = TimeSpan.FromMinutes(10);

    public async Task<Guid> EnsureUserAsync(AuthenticatedUser identity, CancellationToken ct)
    {
        var user = await FindAsync(identity, ct);
        var now = DateTimeOffset.UtcNow;

        if (user is null)
        {
            user = new AppUser
            {
                Id = Guid.CreateVersion7(), TenantId = identity.TenantId, ObjectId = identity.ObjectId,
                DisplayName = Clip(identity.DisplayName), CreatedAt = now, LastSeenAt = now,
            };
            db.AppUsers.Add(user);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                // A parallel first request created the same user; use theirs.
                db.Entry(user).State = EntityState.Detached;
                user = await FindAsync(identity, ct) ?? throw new InvalidOperationException("User row could not be created.");
                return user.Id;
            }
            if (options.Value.ClaimOrphanedData) await ClaimOrphanedAsync(user.Id, ct);
            return user.Id;
        }

        // Keep the display name fresh and note activity, without writing on every request.
        var name = Clip(identity.DisplayName);
        if (now - user.LastSeenAt > SeenInterval || (name is not null && name != user.DisplayName))
        {
            user.LastSeenAt = now;
            if (name is not null) user.DisplayName = name;
            await db.SaveChangesAsync(ct);
        }
        return user.Id;
    }

    private Task<AppUser?> FindAsync(AuthenticatedUser identity, CancellationToken ct) =>
        db.AppUsers.FirstOrDefaultAsync(u => u.TenantId == identity.TenantId && u.ObjectId == identity.ObjectId, ct);

    /// <summary>Hands every ownerless row (data from before sign-in existed) to <paramref name="userId"/>.</summary>
    public async Task ClaimOrphanedAsync(Guid userId, CancellationToken ct)
    {
        await db.Workspaces.IgnoreQueryFilters().Where(w => w.OwnerId == null).ExecuteUpdateAsync(s => s.SetProperty(w => w.OwnerId, userId), ct);
        await db.AiProviders.IgnoreQueryFilters().Where(p => p.OwnerId == null).ExecuteUpdateAsync(s => s.SetProperty(p => p.OwnerId, userId), ct);
        await db.AiModels.IgnoreQueryFilters().Where(m => m.OwnerId == null).ExecuteUpdateAsync(s => s.SetProperty(m => m.OwnerId, userId), ct);
    }

    private static string? Clip(string? s) => s is null ? null : s.Length <= 200 ? s : s[..200];
}
