using System;
using BeServer.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeServer.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260517030000_Phase18EvaluationDatasets")]
public partial class Phase18EvaluationDatasets : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "EvaluationDatasets",
            columns: table => new
            {
                Id = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false),
                NotebookId = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false),
                UserId = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false),
                Name = table.Column<string>(type: "varchar(160)", maxLength: 160, nullable: false),
                Description = table.Column<string>(type: "text", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "datetime", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "datetime", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_EvaluationDatasets", x => x.Id);
                table.ForeignKey("FK_EvaluationDatasets_Notebooks_NotebookId", x => x.NotebookId, "Notebooks", "Id", onDelete: ReferentialAction.Cascade);
                table.ForeignKey("FK_EvaluationDatasets_Users_UserId", x => x.UserId, "Users", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "EvaluationRuns",
            columns: table => new
            {
                Id = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false),
                NotebookId = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false),
                DatasetId = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: true),
                UserId = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false),
                RetrievalVersionAId = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false),
                RetrievalVersionBId = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false),
                SearchModesJson = table.Column<string>(type: "json", nullable: false),
                TopK = table.Column<int>(type: "int", nullable: false),
                HybridAlpha = table.Column<double>(type: "double", nullable: false),
                Status = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false),
                StartedAt = table.Column<DateTime>(type: "datetime", nullable: true),
                CompletedAt = table.Column<DateTime>(type: "datetime", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "datetime", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_EvaluationRuns", x => x.Id);
                table.ForeignKey("FK_EvaluationRuns_EvaluationDatasets_DatasetId", x => x.DatasetId, "EvaluationDatasets", "Id", onDelete: ReferentialAction.SetNull);
                table.ForeignKey("FK_EvaluationRuns_Notebooks_NotebookId", x => x.NotebookId, "Notebooks", "Id", onDelete: ReferentialAction.Cascade);
                table.ForeignKey("FK_EvaluationRuns_Users_UserId", x => x.UserId, "Users", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "EvaluationQueries",
            columns: table => new
            {
                Id = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false),
                DatasetId = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false),
                QueryText = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false),
                ExpectedAnswerNotes = table.Column<string>(type: "text", nullable: true),
                GoldSourceNotes = table.Column<string>(type: "text", nullable: true),
                SortOrder = table.Column<int>(type: "int", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "datetime", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_EvaluationQueries", x => x.Id);
                table.ForeignKey("FK_EvaluationQueries_EvaluationDatasets_DatasetId", x => x.DatasetId, "EvaluationDatasets", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "EvaluationResults",
            columns: table => new
            {
                Id = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false),
                RunId = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false),
                QueryId = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: true),
                QueryTextSnapshot = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false),
                RetrievalVersionId = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false),
                Mode = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false),
                LatencyMs = table.Column<int>(type: "int", nullable: false),
                ResultCount = table.Column<int>(type: "int", nullable: false),
                ResultsJson = table.Column<string>(type: "json", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_EvaluationResults", x => x.Id);
                table.ForeignKey("FK_EvaluationResults_EvaluationQueries_QueryId", x => x.QueryId, "EvaluationQueries", "Id", onDelete: ReferentialAction.SetNull);
                table.ForeignKey("FK_EvaluationResults_EvaluationRuns_RunId", x => x.RunId, "EvaluationRuns", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex("IX_EvaluationDatasets_UserId_NotebookId", "EvaluationDatasets", ["UserId", "NotebookId"]);
        migrationBuilder.CreateIndex("IX_EvaluationDatasets_NotebookId", "EvaluationDatasets", "NotebookId");
        migrationBuilder.CreateIndex("IX_EvaluationQueries_DatasetId_SortOrder", "EvaluationQueries", ["DatasetId", "SortOrder"]);
        migrationBuilder.CreateIndex("IX_EvaluationRuns_DatasetId", "EvaluationRuns", "DatasetId");
        migrationBuilder.CreateIndex("IX_EvaluationRuns_NotebookId", "EvaluationRuns", "NotebookId");
        migrationBuilder.CreateIndex("IX_EvaluationRuns_UserId_NotebookId_CreatedAt", "EvaluationRuns", ["UserId", "NotebookId", "CreatedAt"]);
        migrationBuilder.CreateIndex("IX_EvaluationResults_QueryId", "EvaluationResults", "QueryId");
        migrationBuilder.CreateIndex("IX_EvaluationResults_RunId", "EvaluationResults", "RunId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("EvaluationResults");
        migrationBuilder.DropTable("EvaluationQueries");
        migrationBuilder.DropTable("EvaluationRuns");
        migrationBuilder.DropTable("EvaluationDatasets");
    }
}
