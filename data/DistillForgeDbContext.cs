using DistillForge.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace DistillForge.Data;

public class DistillForgeDbContext(DbContextOptions<DistillForgeDbContext> options) : DbContext(options)
{
    public DbSet<AppUser> AppUsers => Set<AppUser>();
    public DbSet<Workspace> Workspaces => Set<Workspace>();
    public DbSet<Segment> Segments => Set<Segment>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<ActionItem> ActionItems => Set<ActionItem>();
    public DbSet<Evidence> Evidence => Set<Evidence>();
    public DbSet<ActionItemLink> ActionItemLinks => Set<ActionItemLink>();
    public DbSet<ExtractionRun> ExtractionRuns => Set<ExtractionRun>();
    public DbSet<AiProvider> AiProviders => Set<AiProvider>();
    public DbSet<AiModel> AiModels => Set<AiModel>();
    public DbSet<Integration> Integrations => Set<Integration>();
    public DbSet<NotePush> NotePushes => Set<NotePush>();

    /// <summary>
    /// The signed-in user this context acts for. Set once per request by the auth middleware (or by the queue runner, to the
    /// run's owner). Every owned entity has a global query filter on it, so with no user set nothing is visible (default-deny),
    /// and another user's rows simply don't exist as far as queries are concerned. Use <c>IgnoreQueryFilters()</c> deliberately.
    /// </summary>
    public Guid? CurrentUserId { get; set; }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampOwners();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        StampOwners();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>New workspaces, providers, models and integrations belong to the current user. Refuses to save them with no user rather than create ownerless rows.</summary>
    private void StampOwners()
    {
        foreach (var entry in ChangeTracker.Entries().Where(e => e.State == EntityState.Added))
        {
            if (entry.Entity is not (Workspace or AiProvider or AiModel or Integration or NotePush)) continue;
            var owner = entry.Property("OwnerId");
            if (owner.CurrentValue is not null) continue;
            owner.CurrentValue = CurrentUserId
                ?? throw new InvalidOperationException($"Cannot create a {entry.Metadata.ClrType.Name} without a signed-in user.");
        }
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<AppUser>(e =>
        {
            e.Property(x => x.TenantId).HasMaxLength(64);
            e.Property(x => x.ObjectId).HasMaxLength(64);
            e.Property(x => x.DisplayName).HasMaxLength(200);
            e.Property(x => x.Tier).HasConversion<string>().HasMaxLength(16).HasDefaultValue(UserTier.Lite);
            e.HasIndex(x => new { x.TenantId, x.ObjectId }).IsUnique();
        });

        // Ownership. Roots filter on their own OwnerId; children follow their workspace. The `CurrentUserId != null` guard matters:
        // without it a null user would match ownerless rows (OwnerId IS NULL).
        b.Entity<Workspace>().HasQueryFilter(w => CurrentUserId != null && w.OwnerId == CurrentUserId);
        b.Entity<AiProvider>().HasQueryFilter(p => CurrentUserId != null && p.OwnerId == CurrentUserId);
        b.Entity<AiModel>().HasQueryFilter(m => CurrentUserId != null && m.OwnerId == CurrentUserId);
        b.Entity<Integration>().HasQueryFilter(i => CurrentUserId != null && i.OwnerId == CurrentUserId);
        b.Entity<NotePush>().HasQueryFilter(p => CurrentUserId != null && p.OwnerId == CurrentUserId);
        b.Entity<Segment>().HasQueryFilter(x => CurrentUserId != null && x.Workspace!.OwnerId == CurrentUserId);
        b.Entity<Category>().HasQueryFilter(x => CurrentUserId != null && x.Workspace!.OwnerId == CurrentUserId);
        b.Entity<ActionItem>().HasQueryFilter(x => CurrentUserId != null && x.Workspace!.OwnerId == CurrentUserId);
        b.Entity<Evidence>().HasQueryFilter(x => CurrentUserId != null && x.ActionItem!.Workspace!.OwnerId == CurrentUserId);
        b.Entity<ExtractionRun>().HasQueryFilter(x => CurrentUserId != null && x.Workspace!.OwnerId == CurrentUserId);
        b.Entity<ActionItemLink>().HasQueryFilter(x => CurrentUserId != null && x.Workspace!.OwnerId == CurrentUserId);

        b.Entity<Workspace>(e =>
        {
            e.Property(x => x.Title).HasMaxLength(200);
            e.Property(x => x.Strategy).HasConversion<string>().HasMaxLength(32);
            e.HasIndex(x => x.IsArchived);
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.OwnerId);
        });

        b.Entity<Segment>(e =>
        {
            e.Property(x => x.Label).HasMaxLength(16);
            e.HasOne(x => x.Workspace).WithMany(w => w.Segments).HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.WorkspaceId, x.Ordinal }).IsUnique();
            e.HasIndex(x => new { x.WorkspaceId, x.Label }).IsUnique();
        });

        b.Entity<Category>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(100);
            e.Property(x => x.Colour).HasMaxLength(16);
            e.HasOne(x => x.Workspace).WithMany(w => w.Categories).HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ActionItem>(e =>
        {
            e.Property(x => x.Title).HasMaxLength(300);
            e.Property(x => x.Owner).HasMaxLength(200);
            e.Property(x => x.Due).HasMaxLength(100);
            e.Property(x => x.Colour).HasMaxLength(16);
            e.HasOne(x => x.Workspace).WithMany(w => w.ActionItems).HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
            // Removing a category moves its notes back to Uncategorised.
            e.HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => new { x.WorkspaceId, x.CategoryId });
            // Deleting a parent frees its merged notes rather than deleting them.
            e.HasOne<ActionItem>().WithMany().HasForeignKey(x => x.MergedIntoId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<Evidence>(e =>
        {
            e.Property(x => x.MatchQuality).HasConversion<string>().HasMaxLength(16);
            e.HasOne(x => x.ActionItem).WithMany(a => a.Evidence).HasForeignKey(x => x.ActionItemId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Segment).WithMany().HasForeignKey(x => x.SegmentId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ExtractionRun>(e =>
        {
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.Model).HasMaxLength(200);
            e.Property(x => x.Phase).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.MergeLevel).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.FinishReason).HasMaxLength(32);
            // Deleting a model keeps the run (and its Model string) but forgets which config produced it.
            e.HasOne<AiModel>().WithMany().HasForeignKey(x => x.AiModelId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Workspace).WithMany(w => w.ExtractionRuns).HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ActionItemLink>(e =>
        {
            e.Property(x => x.Source).HasConversion<string>().HasMaxLength(16);
            e.HasOne(x => x.Workspace).WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<ActionItem>().WithMany().HasForeignKey(x => x.ActionItemAId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<ActionItem>().WithMany().HasForeignKey(x => x.ActionItemBId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.ActionItemAId, x.ActionItemBId }).IsUnique();
            e.HasIndex(x => x.WorkspaceId);
        });

        b.Entity<AiProvider>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(100);
            e.Property(x => x.BaseUrl).HasMaxLength(500);
            e.Property(x => x.ApiKeyEncrypted).HasMaxLength(4000);
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.OwnerId);
        });

        b.Entity<AiModel>(e =>
        {
            e.Property(x => x.ModelId).HasMaxLength(200);
            e.Property(x => x.DisplayName).HasMaxLength(100);
            e.Property(x => x.ResponseFormat).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.Thinking).HasConversion<string>().HasMaxLength(16).HasDefaultValue(DistillForge.Core.Contracts.ThinkingMode.Default);
            e.HasOne(x => x.Provider).WithMany(p => p.Models).HasForeignKey(x => x.AiProviderId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.AiProviderId, x.ModelId }).IsUnique();
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Restrict);
            // One default model per user.
            e.HasIndex(x => x.OwnerId).IsUnique().HasFilter("\"IsDefault\" = true");
        });

        b.Entity<Integration>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(100);
            e.Property(x => x.Platform).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.SecretsEncrypted).HasMaxLength(8000);
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.OwnerId);
        });

        b.Entity<NotePush>(e =>
        {
            e.Property(x => x.ExternalId).HasMaxLength(200);
            e.Property(x => x.ExternalUrl).HasMaxLength(1000);
            e.Property(x => x.ItemType).HasMaxLength(200);
            e.HasOne<ActionItem>().WithMany().HasForeignKey(x => x.ActionItemId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Integration>().WithMany().HasForeignKey(x => x.IntegrationId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.ActionItemId, x.IntegrationId });
            e.HasIndex(x => x.OwnerId);
        });
    }
}
