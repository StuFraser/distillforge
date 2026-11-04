using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DistillForge.Data;

/// <summary>
/// Used by `dotnet ef` only. The Functions isolated host can't be booted by the EF tools,
/// so the connection string comes from the ConnectionStrings__Postgres env var.
/// Adding a migration doesn't need a live database; `database update` does.
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<DistillForgeDbContext>
{
    public DistillForgeDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
                 ?? "Host=localhost;Database=distillforge;Username=postgres";
        var options = new DbContextOptionsBuilder<DistillForgeDbContext>().UseNpgsql(cs).Options;
        return new DistillForgeDbContext(options);
    }
}
