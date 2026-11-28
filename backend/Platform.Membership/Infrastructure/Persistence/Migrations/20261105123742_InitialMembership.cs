using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Platform.Membership.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialMembership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "membership");

            migrationBuilder.CreateTable(
                name: "CourseMembers",
                schema: "membership",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CourseId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    EnrolledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EnrolledBy = table.Column<Guid>(type: "uuid", nullable: false),
                    LeftAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LeftBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CourseMembers", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CourseMembers_AccountId",
                schema: "membership",
                table: "CourseMembers",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "UX_CourseMembers_Course_Account_Active",
                schema: "membership",
                table: "CourseMembers",
                columns: new[] { "CourseId", "AccountId" },
                unique: true,
                filter: "\"Status\" = 'Active'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CourseMembers",
                schema: "membership");
        }
    }
}
