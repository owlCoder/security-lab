using Microsoft.EntityFrameworkCore;
using Platform.Competencies.Domain;

namespace Platform.Competencies.Infrastructure.Persistence;

public sealed class CompetenciesDbContext(DbContextOptions<CompetenciesDbContext> options) : DbContext(options)
{
    public const string SchemaName = "prg04";
    public DbSet<Competency> Competencies => Set<Competency>();
    public DbSet<LaboratoryCompetency> LaboratoryCompetencies => Set<LaboratoryCompetency>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);
        var competency = modelBuilder.Entity<Competency>();
        competency.ToTable("competencies", table =>
        {
            table.HasCheckConstraint("CK_competencies_id", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid");
            table.HasCheckConstraint("CK_competencies_name", "length(btrim(\"Name\")) > 0");
        });
        competency.HasKey(x => x.Id);
        competency.Property(x => x.Id).ValueGeneratedNever();
        competency.Property(x => x.Name).HasMaxLength(Competency.MaxNameLength).IsRequired();
        competency.Property(x => x.Description).HasMaxLength(Competency.MaxDescriptionLength);
        competency.HasData(BuiltInCompetencies.All);

        var link = modelBuilder.Entity<LaboratoryCompetency>();
        link.ToTable("laboratory_competencies", table =>
            table.HasCheckConstraint("CK_laboratory_competencies_laboratory_id",
                "\"LaboratoryId\" <> '00000000-0000-0000-0000-000000000000'::uuid"));
        link.HasKey(x => new { x.LaboratoryId, x.CompetencyId });
        link.Property(x => x.LaboratoryId).ValueGeneratedNever();
        link.Property(x => x.CompetencyId).ValueGeneratedNever();
        link.HasOne<Competency>().WithMany().HasForeignKey(x => x.CompetencyId).OnDelete(DeleteBehavior.Restrict);
    }
}
