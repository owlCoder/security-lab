using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Platform.LaboratoryPersistence.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLaboratoryCourse : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CourseId",
                schema: "authoring",
                table: "laboratories",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_laboratories_CourseId",
                schema: "authoring",
                table: "laboratories",
                column: "CourseId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_laboratories_CourseId",
                schema: "authoring",
                table: "laboratories",
                sql: "\"CourseId\" IS NULL OR \"CourseId\" <> '00000000-0000-0000-0000-000000000000'::uuid");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_laboratories_CourseId",
                schema: "authoring",
                table: "laboratories");

            migrationBuilder.DropCheckConstraint(
                name: "CK_laboratories_CourseId",
                schema: "authoring",
                table: "laboratories");

            migrationBuilder.DropColumn(
                name: "CourseId",
                schema: "authoring",
                table: "laboratories");
        }
    }
}
