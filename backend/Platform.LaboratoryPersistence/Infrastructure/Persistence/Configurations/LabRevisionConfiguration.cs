using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Platform.LaboratoryPersistence.Infrastructure.Persistence.Entities;

namespace Platform.LaboratoryPersistence.Infrastructure.Persistence.Configurations;

public sealed class LabRevisionConfiguration : IEntityTypeConfiguration<LabRevisionRecord>
{
    public void Configure(EntityTypeBuilder<LabRevisionRecord> builder)
    {
        builder.ToTable("lab_revisions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.HasIndex(x => new { x.LaboratoryId, x.Number }).IsUnique();
        builder.HasOne(x => x.Laboratory).WithMany(x => x.Revisions)
            .HasForeignKey(x => x.LaboratoryId).OnDelete(DeleteBehavior.Restrict);
        builder.ToTable(t => t.HasCheckConstraint("CK_lab_revisions_positive_number", "\"Number\" > 0"));
    }
}
