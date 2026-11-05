using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Platform.Laboratories.Infrastructure.Persistence.Migrations
{
    public partial class AddRevisionState : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LabRevisions",
                schema: "laboratories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CourseId = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RowVersion = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LabRevisions", x => x.Id);
                    table.CheckConstraint("CK_LabRevisions_AuthorId", "\"AuthorId\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("CK_LabRevisions_ContentHash", "\"ContentHash\" ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("CK_LabRevisions_CourseId", "\"CourseId\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("CK_LabRevisions_Id", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("CK_LabRevisions_RowVersion", "\"RowVersion\" > 0");
                    table.CheckConstraint("CK_LabRevisions_Status", "\"Status\" IN (0, 1, 2, 3)");
                });

            migrationBuilder.CreateIndex(
                name: "IX_LabRevisions_CourseId",
                schema: "laboratories",
                table: "LabRevisions",
                column: "CourseId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LabRevisions",
                schema: "laboratories");
        }
    }
}
