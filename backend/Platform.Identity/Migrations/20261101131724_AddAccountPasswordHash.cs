using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Platform.Identity.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountPasswordHash : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "password_hash",
                schema: "identity",
                table: "accounts",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "password_hash",
                schema: "identity",
                table: "accounts");
        }
    }
}
