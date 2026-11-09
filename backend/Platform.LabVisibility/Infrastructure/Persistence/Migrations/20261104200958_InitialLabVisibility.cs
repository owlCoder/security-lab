using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Platform.LabVisibility.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialLabVisibility : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "lab_visibility");

            migrationBuilder.CreateTable(
                name: "lab_public_content",
                schema: "lab_visibility",
                columns: table => new
                {
                    lab_id = table.Column<Guid>(type: "uuid", nullable: false),
                    course_id = table.Column<Guid>(type: "uuid", nullable: false),
                    author_id = table.Column<Guid>(type: "uuid", nullable: false),
                    state = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    title = table.Column<string>(type: "text", nullable: false),
                    goal = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_lab_public_content", x => x.lab_id);
                });

            migrationBuilder.CreateTable(
                name: "lab_public_task",
                schema: "lab_visibility",
                columns: table => new
                {
                    lab_id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_id = table.Column<string>(type: "text", nullable: false),
                    task_order = table.Column<int>(type: "integer", nullable: false),
                    prompt = table.Column<string>(type: "text", nullable: false),
                    options = table.Column<List<string>>(type: "text[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_lab_public_task", x => new { x.lab_id, x.task_id });
                    table.ForeignKey(
                        name: "FK_lab_public_task_lab_public_content_lab_id",
                        column: x => x.lab_id,
                        principalSchema: "lab_visibility",
                        principalTable: "lab_public_content",
                        principalColumn: "lab_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "lab_evaluator_key",
                schema: "lab_visibility",
                columns: table => new
                {
                    lab_id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_id = table.Column<string>(type: "text", nullable: false),
                    expected_answer = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_lab_evaluator_key", x => new { x.lab_id, x.task_id });
                    table.ForeignKey(
                        name: "FK_lab_evaluator_key_lab_public_task_lab_id_task_id",
                        columns: x => new { x.lab_id, x.task_id },
                        principalSchema: "lab_visibility",
                        principalTable: "lab_public_task",
                        principalColumns: new[] { "lab_id", "task_id" },
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "lab_evaluator_key",
                schema: "lab_visibility");

            migrationBuilder.DropTable(
                name: "lab_public_task",
                schema: "lab_visibility");

            migrationBuilder.DropTable(
                name: "lab_public_content",
                schema: "lab_visibility");
        }
    }
}
