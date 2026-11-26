using Microsoft.EntityFrameworkCore;
using Platform.Authoring.Domain;

namespace Platform.Authoring.Infrastructure.Persistence;

/// <summary>Authoring-owned storage, schema "authoring". The module owns its tables; no other module reads them.</summary>
internal sealed class AuthoringDbContext(DbContextOptions<AuthoringDbContext> options) : DbContext(options)
{
    // NOT "authoring": that schema and its __EFMigrationsHistory table belong to Platform.LaboratoryPersistence.
    public const string Schema = "authoring_drafts";

    public DbSet<DraftRow> Drafts => Set<DraftRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<DraftRow>(row =>
        {
            row.ToTable("drafts");
            row.HasKey(d => d.Id);
            row.Property(d => d.Id).ValueGeneratedNever();
            row.Property(d => d.Title).HasMaxLength(LabContent.MaxTitleLength).IsRequired();
            row.Property(d => d.Goal).HasMaxLength(LabContent.MaxGoalLength).IsRequired();
            row.Property(d => d.Version).IsRequired();
            // Listing "my drafts" (AUT-01) filters by owner.
            row.HasIndex(d => d.OwnerId);
        });
    }
}
