using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Platform.Audit.Infrastructure.Persistence;

internal sealed class AuditDbContextFactory : IDesignTimeDbContextFactory<AuditDbContext>
{
    public AuditDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("AUDIT_MIGRATION_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("Set AUDIT_MIGRATION_CONNECTION_STRING before running Audit migrations.");

        var options = new DbContextOptionsBuilder<AuditDbContext>()
            .UseNpgsql(connectionString, postgres =>
                postgres.MigrationsHistoryTable("__EFMigrationsHistory", "audit"))
            .Options;

        return new AuditDbContext(options);
    }
}
