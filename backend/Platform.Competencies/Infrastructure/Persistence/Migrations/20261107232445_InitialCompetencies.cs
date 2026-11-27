using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Platform.Competencies.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCompetencies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "prg04");

            migrationBuilder.CreateTable(
                name: "competencies",
                schema: "prg04",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_competencies", x => x.Id);
                    table.CheckConstraint("CK_competencies_id", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("CK_competencies_name", "length(btrim(\"Name\")) > 0");
                });

            migrationBuilder.CreateTable(
                name: "laboratory_competencies",
                schema: "prg04",
                columns: table => new
                {
                    LaboratoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompetencyId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_laboratory_competencies", x => new { x.LaboratoryId, x.CompetencyId });
                    table.CheckConstraint("CK_laboratory_competencies_laboratory_id", "\"LaboratoryId\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.ForeignKey(
                        name: "FK_laboratory_competencies_competencies_CompetencyId",
                        column: x => x.CompetencyId,
                        principalSchema: "prg04",
                        principalTable: "competencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                schema: "prg04",
                table: "competencies",
                columns: new[] { "Id", "Description", "Name" },
                values: new object[,]
                {
                    { new Guid("4b677e63-204f-4688-a0eb-a339c343f101"), "Prepoznavanje i primena pravila pristupa resursima.", "Kontrola pristupa" },
                    { new Guid("4b677e63-204f-4688-a0eb-a339c343f102"), "Tumačenje događaja i prepoznavanje bezbednosnih nepravilnosti.", "Analiza bezbednosnih logova" },
                    { new Guid("4b677e63-204f-4688-a0eb-a339c343f103"), "Prepoznavanje i otklanjanje nebezbednih podešavanja.", "Bezbedna konfiguracija" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_laboratory_competencies_CompetencyId",
                schema: "prg04",
                table: "laboratory_competencies",
                column: "CompetencyId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "laboratory_competencies",
                schema: "prg04");

            migrationBuilder.DropTable(
                name: "competencies",
                schema: "prg04");
        }
    }
}
