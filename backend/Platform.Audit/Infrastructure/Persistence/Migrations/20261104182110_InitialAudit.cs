using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Platform.Audit.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "audit");

            migrationBuilder.CreateTable(
                name: "audit_events",
                schema: "audit",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actor_kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    event_type = table.Column<string>(type: "text", nullable: false),
                    outcome = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    course_id = table.Column<Guid>(type: "uuid", nullable: true),
                    resource_type = table.Column<string>(type: "text", nullable: true),
                    resource_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    correlation_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    details = table.Column<string>(type: "jsonb", nullable: false),
                    schema_version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_events", x => x.id);
                    table.CheckConstraint("ck_audit_events_actor", "(actor_kind = 'Anonymous' AND actor_id IS NULL) OR (actor_kind IN ('User', 'Service') AND actor_id IS NOT NULL AND actor_id <> '00000000-0000-0000-0000-000000000000'::uuid)");
                    table.CheckConstraint("ck_audit_events_details_object", "jsonb_typeof(details) = 'object'");
                    table.CheckConstraint("ck_audit_events_outcome", "outcome IN ('Success', 'Denied', 'Failure')");
                    table.CheckConstraint("ck_audit_events_resource", "(resource_type IS NULL AND resource_id IS NULL) OR (resource_type IS NOT NULL AND resource_id IS NOT NULL)");
                    table.CheckConstraint("ck_audit_events_schema_version", "schema_version >= 1");
                });

            migrationBuilder.CreateIndex(
                name: "ix_audit_events_correlation_id",
                schema: "audit",
                table: "audit_events",
                column: "correlation_id");

            migrationBuilder.CreateIndex(
                name: "ix_audit_events_course_time_id",
                schema: "audit",
                table: "audit_events",
                columns: new[] { "course_id", "occurred_at", "id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_events",
                schema: "audit");
        }
    }
}
