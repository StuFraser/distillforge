using DistillForge.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace DistillForge.Data;

public class DistillForgeDbContext(DbContextOptions<DistillForgeDbContext> options) : DbContext(options)
{
    public DbSet<Workspace> Workspaces => Set<Workspace>();
    public DbSet<Segment> Segments => Set<Segment>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<ActionItem> ActionItems => Set<ActionItem>();
    public DbSet<Evidence> Evidence => Set<Evidence>();
    public DbSet<ActionItemLink> ActionItemLinks => Set<ActionItemLink>();
    public DbSet<ExtractionRun> ExtractionRuns => Set<ExtractionRun>();
    public DbSet<AiProvider> AiProviders => Set<AiProvider>();
    public DbSet<AiModel> AiModels => Set<AiModel>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Workspace>(e =>
        {
            e.Property(x => x.Title).HasMaxLength(200);
            e.Property(x => x.Strategy).HasConversion<string>().HasMaxLength(32);
            e.HasIndex(x => x.IsArchived);
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
            e.Property(x => x.FinishReason).HasMaxLength(32);
            // Deleting a model keeps the run (and its Model string) but forgets which config produced it.
            e.HasOne<AiModel>().WithMany().HasForeignKey(x => x.AiModelId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Workspace).WithMany(w => w.ExtractionRuns).HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ActionItemLink>(e =>
        {
            e.Property(x => x.Source).HasConversion<string>().HasMaxLength(16);
            e.HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
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
        });

        b.Entity<AiModel>(e =>
        {
            e.Property(x => x.ModelId).HasMaxLength(200);
            e.Property(x => x.DisplayName).HasMaxLength(100);
            e.Property(x => x.ResponseFormat).HasConversion<string>().HasMaxLength(16);
            e.HasOne(x => x.Provider).WithMany(p => p.Models).HasForeignKey(x => x.AiProviderId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.AiProviderId, x.ModelId }).IsUnique();
            e.HasIndex(x => x.IsDefault).IsUnique().HasFilter("\"IsDefault\" = true");
        });
    }
}
