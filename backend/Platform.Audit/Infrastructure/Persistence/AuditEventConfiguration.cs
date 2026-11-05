using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Platform.Audit.Infrastructure.Persistence;

internal sealed class AuditEventConfiguration : IEntityTypeConfiguration<AuditEventRow>
{
    public void Configure(EntityTypeBuilder<AuditEventRow> builder)
    {
        builder.ToTable("audit_events", "audit", table =>
        {
            table.HasCheckConstraint("ck_audit_events_actor",
                "(actor_kind = 'Anonymous' AND actor_id IS NULL) OR " +
                "(actor_kind IN ('User', 'Service') AND actor_id IS NOT NULL " +
                "AND actor_id <> '00000000-0000-0000-0000-000000000000'::uuid)");
            table.HasCheckConstraint("ck_audit_events_outcome", "outcome IN ('Success', 'Denied', 'Failure')");
            table.HasCheckConstraint("ck_audit_events_schema_version", "schema_version >= 1");
            table.HasCheckConstraint("ck_audit_events_details_object", "jsonb_typeof(details) = 'object'");
            table.HasCheckConstraint("ck_audit_events_resource",
                "(resource_type IS NULL AND resource_id IS NULL) OR " +
                "(resource_type IS NOT NULL AND resource_id IS NOT NULL)");
        });

        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(row => row.OccurredAt).HasColumnName("occurred_at").HasColumnType("timestamp with time zone");
        builder.Property(row => row.ActorKind).HasColumnName("actor_kind").HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(row => row.ActorId).HasColumnName("actor_id");
        // Event types are catalog-controlled; text avoids imposing a second catalog length limit.
        builder.Property(row => row.EventType).HasColumnName("event_type").IsRequired();
        builder.Property(row => row.Outcome).HasColumnName("outcome").HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(row => row.CourseId).HasColumnName("course_id");
        builder.Property(row => row.ResourceType).HasColumnName("resource_type");
        builder.Property(row => row.ResourceId).HasColumnName("resource_id").HasMaxLength(64);
        builder.Property(row => row.CorrelationId).HasColumnName("correlation_id").HasMaxLength(64).IsRequired();
        builder.Property(row => row.DetailsJson).HasColumnName("details").HasColumnType("jsonb").IsRequired();
        builder.Property(row => row.SchemaVersion).HasColumnName("schema_version");

        builder.HasIndex(row => new { row.CourseId, row.OccurredAt, row.Id })
            .HasDatabaseName("ix_audit_events_course_time_id");
        builder.HasIndex(row => row.CorrelationId).HasDatabaseName("ix_audit_events_correlation_id");
        // Historical identifiers intentionally have no cascading foreign keys to other modules.
    }
}
