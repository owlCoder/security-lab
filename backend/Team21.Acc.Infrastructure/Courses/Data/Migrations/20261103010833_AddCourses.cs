using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Team21.Acc.Infrastructure.Courses.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCourses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "courses");

            migrationBuilder.CreateTable(
                name: "Courses",
                schema: "courses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ArchivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ArchivedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Courses", x => x.Id);
                    table.CheckConstraint("CK_Courses_ArchiveMetadata", "(\"Status\" = 'Active' AND \"ArchivedAt\" IS NULL AND \"ArchivedBy\" IS NULL) OR (\"Status\" = 'Archived' AND \"ArchivedAt\" IS NOT NULL AND \"ArchivedBy\" IS NOT NULL AND \"ArchivedBy\" <> '00000000-0000-0000-0000-000000000000')");
                    table.CheckConstraint("CK_Courses_Code", "length(trim(\"Code\")) BETWEEN 1 AND 32 AND length(\"Code\") <= 32 AND \"Code\" = upper(\"Code\")");
                    table.CheckConstraint("CK_Courses_CreatedBy", "\"CreatedBy\" <> '00000000-0000-0000-0000-000000000000'");
                    table.CheckConstraint("CK_Courses_Description", "length(\"Description\") <= 4000");
                    table.CheckConstraint("CK_Courses_Name", "length(trim(\"Name\")) BETWEEN 1 AND 200 AND length(\"Name\") <= 200");
                    table.CheckConstraint("CK_Courses_Status", "\"Status\" IN ('Active', 'Archived')");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Courses_Code",
                schema: "courses",
                table: "Courses",
                column: "Code",
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_Courses_Status_Name",
                schema: "courses",
                table: "Courses",
                columns: new[] { "Status", "Name" },
                filter: "\"IsDeleted\" = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Courses",
                schema: "courses");
        }
    }
}
