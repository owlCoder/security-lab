using Microsoft.EntityFrameworkCore;

namespace Platform.Audit.Infrastructure.Persistence;

/// <summary>Audit-owned storage. The caller owns transaction and SaveChanges boundaries.</summary>
internal sealed class AuditDbContext(DbContextOptions<AuditDbContext> options) : DbContext(options)
{
    public DbSet<AuditEventRow> Events => Set<AuditEventRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new AuditEventConfiguration());
    }
}
