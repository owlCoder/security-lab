using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Platform.LaboratoryPersistence.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AlignLaboratoryPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Type",
                schema: "authoring",
                table: "laboratories",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);

            migrationBuilder.AlterColumn<string>(
                name: "Title",
                schema: "authoring",
                table: "laboratories",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(120)",
                oldMaxLength: 120);

            migrationBuilder.AlterColumn<string>(
                name: "Goal",
                schema: "authoring",
                table: "laboratories",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(2000)",
                oldMaxLength: 2000);

            migrationBuilder.AlterColumn<string>(
                name: "Difficulty",
                schema: "authoring",
                table: "laboratories",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CreatedAt",
                schema: "authoring",
                table: "laboratories",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedBy",
                schema: "authoring",
                table: "laboratories",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "Status",
                schema: "authoring",
                table: "laboratories",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "lab_revisions",
                schema: "authoring",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LaboratoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_lab_revisions", x => x.Id);
                    table.CheckConstraint("CK_lab_revisions_positive_number", "\"Number\" > 0");
                    table.ForeignKey(
                        name: "FK_lab_revisions_laboratories_LaboratoryId",
                        column: x => x.LaboratoryId,
                        principalSchema: "authoring",
                        principalTable: "laboratories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_lab_revisions_LaboratoryId_Number",
                schema: "authoring",
                table: "lab_revisions",
                columns: new[] { "LaboratoryId", "Number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "lab_revisions",
                schema: "authoring");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                schema: "authoring",
                table: "laboratories");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                schema: "authoring",
                table: "laboratories");

            migrationBuilder.DropColumn(
                name: "Status",
                schema: "authoring",
                table: "laboratories");

            migrationBuilder.AlterColumn<string>(
                name: "Type",
                schema: "authoring",
                table: "laboratories",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Title",
                schema: "authoring",
                table: "laboratories",
                type: "character varying(120)",
                maxLength: 120,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "Goal",
                schema: "authoring",
                table: "laboratories",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(2000)",
                oldMaxLength: 2000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Difficulty",
                schema: "authoring",
                table: "laboratories",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30,
                oldNullable: true);
        }
    }
}
