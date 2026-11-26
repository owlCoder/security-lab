using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Platform.Authoring.Infrastructure.Persistence;

/// <summary>Design-time only (dotnet ef). The connection string comes from the environment, never from the repository.</summary>
internal sealed class AuthoringDbContextFactory : IDesignTimeDbContextFactory<AuthoringDbContext>
{
    public AuthoringDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("AUTHORING_MIGRATION_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("Set AUTHORING_MIGRATION_CONNECTION_STRING before running Authoring migrations.");

        var options = new DbContextOptionsBuilder<AuthoringDbContext>()
            .UseNpgsql(connectionString, postgres => postgres.MigrationsHistoryTable("__EFMigrationsHistory", AuthoringDbContext.Schema))
            .Options;

        return new AuthoringDbContext(options);
    }
}
