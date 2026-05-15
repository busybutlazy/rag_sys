using System;
using BeServer.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeServer.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260514020000_Phase9IngestionJobs")]
    public partial class Phase9IngestionJobs : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "IngestionJobs",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false),
                    SourceId = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false),
                    NotebookId = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false),
                    UserId = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false),
                    JobType = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    AttemptCount = table.Column<int>(type: "int", nullable: false),
                    MaxAttempts = table.Column<int>(type: "int", nullable: false),
                    LastError = table.Column<string>(type: "text", nullable: true),
                    AvailableAt = table.Column<DateTime>(type: "datetime", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "datetime", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "datetime", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IngestionJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IngestionJobs_Notebooks_NotebookId",
                        column: x => x.NotebookId,
                        principalTable: "Notebooks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_IngestionJobs_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IngestionJobs_NotebookId",
                table: "IngestionJobs",
                column: "NotebookId");

            migrationBuilder.CreateIndex(
                name: "IX_IngestionJobs_SourceId",
                table: "IngestionJobs",
                column: "SourceId");

            migrationBuilder.CreateIndex(
                name: "IX_IngestionJobs_Status_JobType_AvailableAt",
                table: "IngestionJobs",
                columns: new[] { "Status", "JobType", "AvailableAt" });

            migrationBuilder.CreateIndex(
                name: "IX_IngestionJobs_UserId_NotebookId",
                table: "IngestionJobs",
                columns: new[] { "UserId", "NotebookId" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "IngestionJobs");
        }
    }
}
