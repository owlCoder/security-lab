using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Platform.Membership.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCourseDelegations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CourseDelegations",
                schema: "membership",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CourseId = table.Column<Guid>(type: "uuid", nullable: false),
                    GranteeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    GrantedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    GrantedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevokedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ExpiryRecordedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CourseDelegations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CourseDelegations_CourseId_GranteeId",
                schema: "membership",
                table: "CourseDelegations",
                columns: new[] { "CourseId", "GranteeId" });

            migrationBuilder.CreateIndex(
                name: "IX_CourseDelegations_PendingExpiry",
                schema: "membership",
                table: "CourseDelegations",
                column: "ExpiresAt",
                filter: "\"RevokedAt\" IS NULL AND \"ExpiryRecordedAt\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CourseDelegations",
                schema: "membership");
        }
    }
}
