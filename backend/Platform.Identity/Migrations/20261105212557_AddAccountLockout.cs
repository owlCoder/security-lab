using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Platform.Identity.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountLockout : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "failed_login_count",
                schema: "identity",
                table: "accounts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "lockout_end",
                schema: "identity",
                table: "accounts",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "failed_login_count",
                schema: "identity",
                table: "accounts");

            migrationBuilder.DropColumn(
                name: "lockout_end",
                schema: "identity",
                table: "accounts");
        }
    }
}
