using Microsoft.EntityFrameworkCore;
using Platform.LaboratoryPersistence.Infrastructure.Persistence.Entities;

namespace Platform.LaboratoryPersistence.Infrastructure.Persistence;

public sealed class LaboratoryDbContext(DbContextOptions<LaboratoryDbContext> options) : DbContext(options)
{
    public DbSet<LaboratoryRecord> Laboratories => Set<LaboratoryRecord>();
    public DbSet<LabPrerequisiteRecord> LabPrerequisites => Set<LabPrerequisiteRecord>();
    public DbSet<LabRevisionRecord> LabRevisions => Set<LabRevisionRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema("authoring"); // Keep the existing database schema/migration history.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(LaboratoryDbContext).Assembly);
    }
}
