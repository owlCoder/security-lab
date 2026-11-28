using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Platform.Identity.Migrations
{
    /// <inheritdoc />
    public partial class AddMfaCredentials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "mfa_credentials",
                schema: "identity",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    mfa_enabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    totp_secret_ciphertext = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    totp_secret_key_version = table.Column<int>(type: "integer", nullable: true),
                    pending_totp_secret_ciphertext = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    pending_totp_secret_key_version = table.Column<int>(type: "integer", nullable: true),
                    pending_enrollment_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_totp_time_step = table.Column<long>(type: "bigint", nullable: true),
                    mfa_failed_attempts = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    mfa_locked_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mfa_credentials", x => x.user_id);
                    table.ForeignKey(
                        name: "FK_mfa_credentials_accounts_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_mfa_credentials_user_id",
                schema: "identity",
                table: "mfa_credentials",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mfa_credentials",
                schema: "identity");
        }
    }
}
