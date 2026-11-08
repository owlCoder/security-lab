using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Platform.AssignmentPrerequisites.Domain;

namespace Platform.AssignmentPrerequisites.Infrastructure.Persistence;

public sealed class AssignmentPrerequisiteConfiguration : IEntityTypeConfiguration<AssignmentPrerequisiteRow>
{
    public const string TableName = "prerequisites";

    public void Configure(EntityTypeBuilder<AssignmentPrerequisiteRow> builder)
    {
        builder.ToTable(TableName, table =>
        {
            table.HasCheckConstraint(
                "ck_prerequisites_not_self",
                "assignment_id <> required_assignment_id");

            table.HasCheckConstraint(
                "ck_prerequisites_completion_requirement",
                "completion_requirement IN (1, 2)");

            table.HasCheckConstraint(
                "ck_prerequisites_course_id_format",
                "course_id ~ '^[A-Za-z0-9_-]{1,64}$'");
        });

        builder.HasKey(p => new { p.AssignmentId, p.RequiredAssignmentId })
            .HasName("pk_prerequisites");

        builder.Property(p => p.AssignmentId)
            .HasColumnName("assignment_id");

        builder.Property(p => p.RequiredAssignmentId)
            .HasColumnName("required_assignment_id");

        builder.Property(p => p.CourseId)
            .HasColumnName("course_id")
            .HasMaxLength(Identifiers.MaxLength)
            .IsRequired();

        builder.Property(p => p.CompletionRequirement)
            .HasColumnName("completion_requirement")
            .IsRequired();

        builder.Property(p => p.CreatedById)
            .HasColumnName("created_by_id")
            .HasMaxLength(Identifiers.MaxLength)
            .IsRequired();

        builder.Property(p => p.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired();

        builder.HasIndex(p => p.CourseId)
            .HasDatabaseName("ix_prerequisites_course_id");

        builder.HasIndex(p => p.RequiredAssignmentId)
            .HasDatabaseName("ix_prerequisites_required_assignment_id");
    }
}
