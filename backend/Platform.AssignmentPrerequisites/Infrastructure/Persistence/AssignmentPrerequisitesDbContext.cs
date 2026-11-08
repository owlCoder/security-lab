using Microsoft.EntityFrameworkCore;

namespace Platform.AssignmentPrerequisites.Infrastructure.Persistence;

public sealed class AssignmentPrerequisitesDbContext(DbContextOptions<AssignmentPrerequisitesDbContext> options)
    : DbContext(options)
{
    public const string Schema = "assignment_prerequisites";

    public DbSet<AssignmentPrerequisiteRow> Prerequisites => Set<AssignmentPrerequisiteRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfiguration(new AssignmentPrerequisiteConfiguration());
    }
}

public static class AssignmentPrerequisitesDbContextOptions
{
    public static DbContextOptionsBuilder UseAssignmentPrerequisitesNpgsql(
        this DbContextOptionsBuilder builder,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        return builder.UseNpgsql(connectionString, npgsql =>
            npgsql.MigrationsHistoryTable("__EFMigrationsHistory", AssignmentPrerequisitesDbContext.Schema));
    }
}
