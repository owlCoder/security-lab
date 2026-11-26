using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Platform.Authoring.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialAuthoring : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "authoring_drafts");

            migrationBuilder.CreateTable(
                name: "drafts",
                schema: "authoring_drafts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceTemplateId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Goal = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    LabType = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_drafts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_drafts_OwnerId",
                schema: "authoring_drafts",
                table: "drafts",
                column: "OwnerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "drafts",
                schema: "authoring_drafts");
        }
    }
}
