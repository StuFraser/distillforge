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
    public DbSet<ExtractionRun> ExtractionRuns => Set<ExtractionRun>();

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
            e.HasOne(x => x.Workspace).WithMany(w => w.ActionItems).HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
            // Removing a category moves its notes back to Uncategorised.
            e.HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => new { x.WorkspaceId, x.CategoryId });
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
            e.HasOne(x => x.Workspace).WithMany(w => w.ExtractionRuns).HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
