using Microsoft.EntityFrameworkCore;
using Platform.Laboratories.Domain.Models;

namespace Platform.Laboratories.Infrastructure.Persistence;

public sealed class LaboratoriesDbContext(DbContextOptions<LaboratoriesDbContext> options)
    : DbContext(options)
{
    public const string SchemaName = "laboratories";

    public DbSet<LabRevision> LabRevisions => Set<LabRevision>();
    public DbSet<CoursePublishPolicy> CoursePublishPolicies => Set<CoursePublishPolicy>();
    public DbSet<RevisionDecisionRecord> RevisionDecisions => Set<RevisionDecisionRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema(SchemaName);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(LaboratoriesDbContext).Assembly);
    }
}
