using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Platform.Laboratories.Infrastructure.Persistence.Migrations
{
    public partial class CreateReviewTables : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "laboratories");

            migrationBuilder.CreateTable(
                name: "CoursePublishPolicies",
                schema: "laboratories",
                columns: table => new
                {
                    CourseId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequiresIndependentApproval = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CoursePublishPolicies", x => x.CourseId);
                    table.CheckConstraint("CK_CoursePublishPolicies_CourseId", "\"CourseId\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                });

            migrationBuilder.CreateTable(
                name: "RevisionDecisions",
                schema: "laboratories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReviewerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Decision = table.Column<int>(type: "integer", nullable: false),
                    DecidedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RequiresIndependentApproval = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RevisionDecisions", x => x.Id);
                    table.CheckConstraint("CK_RevisionDecisions_ContentHash", "\"ContentHash\" ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("CK_RevisionDecisions_Decision", "\"Decision\" IN (1, 2)");
                    table.CheckConstraint("CK_RevisionDecisions_Id", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("CK_RevisionDecisions_ReviewerId", "\"ReviewerId\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("CK_RevisionDecisions_RevisionId", "\"RevisionId\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                });

            migrationBuilder.CreateIndex(
                name: "IX_RevisionDecisions_RevisionId_DecidedAt",
                schema: "laboratories",
                table: "RevisionDecisions",
                columns: new[] { "RevisionId", "DecidedAt" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CoursePublishPolicies",
                schema: "laboratories");

            migrationBuilder.DropTable(
                name: "RevisionDecisions",
                schema: "laboratories");
        }
    }
}
