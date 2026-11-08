using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Platform.AssignmentPrerequisites.Infrastructure.Persistence;

public sealed class AssignmentPrerequisitesDbContextFactory
    : IDesignTimeDbContextFactory<AssignmentPrerequisitesDbContext>
{
    public AssignmentPrerequisitesDbContext CreateDbContext(string[] args)
    {
        string connectionString = LocalConnectionString.Find()
            ?? throw new InvalidOperationException(
                "Connection string not found. Copy .env.example to .env (see docs/DEVELOPMENT.md) " +
                $"or set {LocalConnectionString.AssignmentsKey}.");

        var options = new DbContextOptionsBuilder<AssignmentPrerequisitesDbContext>();
        options.UseAssignmentPrerequisitesNpgsql(connectionString);

        return new AssignmentPrerequisitesDbContext(options.Options);
    }
}
