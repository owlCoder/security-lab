using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Platform.Laboratories.Domain.Models;
using Platform.Laboratories.Domain.Rules;

namespace Platform.Laboratories.Infrastructure.Persistence.Configurations;

public sealed class LabRevisionConfiguration : IEntityTypeConfiguration<LabRevision>
{
    public void Configure(EntityTypeBuilder<LabRevision> builder)
    {
        builder.ToTable("LabRevisions", table =>
        {
            table.HasCheckConstraint("CK_LabRevisions_Id", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid");
            table.HasCheckConstraint("CK_LabRevisions_CourseId", "\"CourseId\" <> '00000000-0000-0000-0000-000000000000'::uuid");
            table.HasCheckConstraint("CK_LabRevisions_AuthorId", "\"AuthorId\" <> '00000000-0000-0000-0000-000000000000'::uuid");
            table.HasCheckConstraint("CK_LabRevisions_Status", "\"Status\" IN (0, 1, 2, 3)");
            table.HasCheckConstraint("CK_LabRevisions_RowVersion", "\"RowVersion\" > 0");
            table.HasCheckConstraint("CK_LabRevisions_ContentHash", "\"ContentHash\" ~ '^[0-9a-f]{64}$'");
        });
        builder.HasKey(revision => revision.Id);
        builder.Property(revision => revision.Id).ValueGeneratedNever();
        builder.Property(revision => revision.CourseId).IsRequired();
        builder.Property(revision => revision.AuthorId).IsRequired();
        builder.Property(revision => revision.Status).HasConversion<int>().IsRequired();
        builder.Property(revision => revision.ContentHash).HasMaxLength(RevisionContentHash.HexLength).IsRequired();
        builder.Property(revision => revision.RowVersion).IsConcurrencyToken().ValueGeneratedNever();
        builder.HasIndex(revision => revision.CourseId);
    }
}
