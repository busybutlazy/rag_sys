using System;
using BeServer.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeServer.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260517020000_Phase17ReindexJobs")]
public partial class Phase17ReindexJobs : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ReindexJobs",
            columns: table => new
            {
                Id = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false),
                NotebookId = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false),
                UserId = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false),
                SourceId = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: true),
                Scope = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false),
                TargetRetrievalVersionId = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false),
                PreviousRetrievalVersionId = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: true),
                Status = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false),
                SourcesTotal = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                SourcesSucceeded = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                SourcesFailed = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                AttemptCount = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                MaxAttempts = table.Column<int>(type: "int", nullable: false, defaultValue: 3),
                LastError = table.Column<string>(type: "text", nullable: true),
                AvailableAt = table.Column<DateTime>(type: "datetime", nullable: false),
                StartedAt = table.Column<DateTime>(type: "datetime", nullable: true),
                CompletedAt = table.Column<DateTime>(type: "datetime", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "datetime", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "datetime", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ReindexJobs", x => x.Id);
                table.ForeignKey("FK_ReindexJobs_Notebooks_NotebookId", x => x.NotebookId, "Notebooks", "Id", onDelete: ReferentialAction.Cascade);
                table.ForeignKey("FK_ReindexJobs_Users_UserId", x => x.UserId, "Users", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex("IX_ReindexJobs_NotebookId", "ReindexJobs", "NotebookId");
        migrationBuilder.CreateIndex("IX_ReindexJobs_Status_AvailableAt", "ReindexJobs", ["Status", "AvailableAt"]);
        migrationBuilder.CreateIndex("IX_ReindexJobs_UserId_NotebookId", "ReindexJobs", ["UserId", "NotebookId"]);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("ReindexJobs");
    }
}
