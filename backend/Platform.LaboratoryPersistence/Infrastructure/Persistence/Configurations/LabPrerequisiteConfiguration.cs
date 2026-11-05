
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Platform.LaboratoryPersistence.Infrastructure.Persistence.Entities;

namespace Platform.LaboratoryPersistence.Infrastructure.Persistence.Configurations;

public sealed class LabPrerequisiteConfiguration
    : IEntityTypeConfiguration<LabPrerequisiteRecord>
{
    public void Configure(
        EntityTypeBuilder<LabPrerequisiteRecord> builder)
    {
        builder.ToTable("lab_prerequisites");

        builder.HasKey(p => new
        {
            p.LaboratoryId,
            p.PrerequisiteLabId
        });

        builder.HasOne(p => p.Laboratory)
            .WithMany(l => l.Prerequisites)
            .HasForeignKey(p => p.LaboratoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.PrerequisiteLab)
            .WithMany()
            .HasForeignKey(p => p.PrerequisiteLabId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.ToTable(t => t.HasCheckConstraint(
            "CK_lab_prerequisites_no_self_reference",
            "\"LaboratoryId\" <> \"PrerequisiteLabId\""));
    }
}
