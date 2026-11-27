using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Platform.Competencies.Infrastructure.Persistence;

public sealed class CompetenciesDbContextFactory : IDesignTimeDbContextFactory<CompetenciesDbContext>
{
    public CompetenciesDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("PRG04_MIGRATION_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("Set PRG04_MIGRATION_CONNECTION_STRING before running PRG-04 migrations.");
        return new(new DbContextOptionsBuilder<CompetenciesDbContext>()
            .UseNpgsql(connectionString, postgres => postgres.MigrationsHistoryTable("__EFMigrationsHistory", CompetenciesDbContext.SchemaName))
            .Options);
    }
}
