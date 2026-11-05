using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Platform.LaboratoryPersistence.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialLaboratoryPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "authoring");

            migrationBuilder.CreateTable(
                name: "laboratories",
                schema: "authoring",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Goal = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Difficulty = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Tags = table.Column<List<string>>(type: "text[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_laboratories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "lab_prerequisites",
                schema: "authoring",
                columns: table => new
                {
                    LaboratoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    PrerequisiteLabId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_lab_prerequisites", x => new { x.LaboratoryId, x.PrerequisiteLabId });
                    table.CheckConstraint("CK_lab_prerequisites_no_self_reference", "\"LaboratoryId\" <> \"PrerequisiteLabId\"");
                    table.ForeignKey(
                        name: "FK_lab_prerequisites_laboratories_LaboratoryId",
                        column: x => x.LaboratoryId,
                        principalSchema: "authoring",
                        principalTable: "laboratories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_lab_prerequisites_laboratories_PrerequisiteLabId",
                        column: x => x.PrerequisiteLabId,
                        principalSchema: "authoring",
                        principalTable: "laboratories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_lab_prerequisites_PrerequisiteLabId",
                schema: "authoring",
                table: "lab_prerequisites",
                column: "PrerequisiteLabId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "lab_prerequisites",
                schema: "authoring");

            migrationBuilder.DropTable(
                name: "laboratories",
                schema: "authoring");
        }
    }
}
