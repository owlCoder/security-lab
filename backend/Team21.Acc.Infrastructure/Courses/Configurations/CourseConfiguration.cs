using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Team21.Acc.Core.Courses;

namespace Team21.Acc.Infrastructure.Courses.Configurations;

public class CourseConfiguration : IEntityTypeConfiguration<Course>
{
    public void Configure(EntityTypeBuilder<Course> builder)
    {
        builder.ToTable("Courses", table =>
        {
            table.HasCheckConstraint("CK_Courses_Code",
                $"length(trim(\"Code\")) BETWEEN 1 AND {Course.MaxCodeLength} AND length(\"Code\") <= {Course.MaxCodeLength} AND \"Code\" = upper(\"Code\")");
            table.HasCheckConstraint("CK_Courses_Name",
                $"length(trim(\"Name\")) BETWEEN 1 AND {Course.MaxNameLength} AND length(\"Name\") <= {Course.MaxNameLength}");
            table.HasCheckConstraint("CK_Courses_Description",
                $"length(\"Description\") <= {Course.MaxDescriptionLength}");
            table.HasCheckConstraint("CK_Courses_Status", "\"Status\" IN ('Active', 'Archived')");
            table.HasCheckConstraint("CK_Courses_CreatedBy",
                "\"CreatedBy\" <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_Courses_ArchiveMetadata",
                "(\"Status\" = 'Active' AND \"ArchivedAt\" IS NULL AND \"ArchivedBy\" IS NULL) OR " +
                "(\"Status\" = 'Archived' AND \"ArchivedAt\" IS NOT NULL AND \"ArchivedBy\" IS NOT NULL " +
                "AND \"ArchivedBy\" <> '00000000-0000-0000-0000-000000000000')");
        });

        builder.HasKey(course => course.Id);
        builder.Property(course => course.Id).ValueGeneratedNever();
        builder.Property(course => course.Code).IsRequired().HasMaxLength(Course.MaxCodeLength);
        builder.Property(course => course.Name).IsRequired().HasMaxLength(Course.MaxNameLength);
        builder.Property(course => course.Description).IsRequired().HasMaxLength(Course.MaxDescriptionLength);
        builder.Property(course => course.Status).HasConversion<string>().HasMaxLength(16).IsRequired()
            .IsConcurrencyToken();
        builder.Property(course => course.CreatedBy).IsRequired();
        builder.Ignore(course => course.CanEnroll);

        builder.HasIndex(course => course.Code).IsUnique().HasFilter("\"IsDeleted\" = false");

        // Course names need not be unique; different teaching periods may share a name.
        builder.HasIndex(course => new { course.Status, course.Name })
            .HasFilter("\"IsDeleted\" = false");
    }
}
