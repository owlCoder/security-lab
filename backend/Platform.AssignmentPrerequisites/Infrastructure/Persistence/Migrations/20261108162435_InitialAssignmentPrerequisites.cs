using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Platform.AssignmentPrerequisites.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialAssignmentPrerequisites : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "assignment_prerequisites");

            migrationBuilder.CreateTable(
                name: "prerequisites",
                schema: "assignment_prerequisites",
                columns: table => new
                {
                    assignment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    required_assignment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    course_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    completion_requirement = table.Column<short>(type: "smallint", nullable: false),
                    created_by_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_prerequisites", x => new { x.assignment_id, x.required_assignment_id });
                    table.CheckConstraint("ck_prerequisites_completion_requirement", "completion_requirement IN (1, 2)");
                    table.CheckConstraint("ck_prerequisites_course_id_format", "course_id ~ '^[A-Za-z0-9_-]{1,64}$'");
                    table.CheckConstraint("ck_prerequisites_not_self", "assignment_id <> required_assignment_id");
                });

            migrationBuilder.CreateIndex(
                name: "ix_prerequisites_course_id",
                schema: "assignment_prerequisites",
                table: "prerequisites",
                column: "course_id");

            migrationBuilder.CreateIndex(
                name: "ix_prerequisites_required_assignment_id",
                schema: "assignment_prerequisites",
                table: "prerequisites",
                column: "required_assignment_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "prerequisites",
                schema: "assignment_prerequisites");
        }
    }
}
