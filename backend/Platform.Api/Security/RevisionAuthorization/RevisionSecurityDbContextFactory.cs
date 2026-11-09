using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Platform.Api.Security.RevisionAuthorization;

/// <summary>Migration credentials are separate from the application's runtime connection.</summary>
public sealed class RevisionSecurityDbContextFactory : IDesignTimeDbContextFactory<RevisionSecurityDbContext>
{
    public RevisionSecurityDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("REVISION_SECURITY_MIGRATION_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connection))
            throw new InvalidOperationException("Set REVISION_SECURITY_MIGRATION_CONNECTION_STRING for revision security migrations.");
        return new RevisionSecurityDbContext(new DbContextOptionsBuilder<RevisionSecurityDbContext>()
            .UseNpgsql(connection, postgres => postgres.MigrationsHistoryTable("__EFMigrationsHistory", "revision_security"))
            .Options);
    }
}
