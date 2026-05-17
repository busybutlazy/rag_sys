using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeServer.Migrations;

public partial class Phase16LabFoundations : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>("IsDevAdmin", "Users", type: "tinyint(1)", nullable: false, defaultValue: false);
        migrationBuilder.AddColumn<string>("ActiveRetrievalVersionId", "Notebooks", type: "varchar(36)", maxLength: 36, nullable: true);
        migrationBuilder.AddColumn<string>("ActiveRetrievalVersionId", "Sources", type: "varchar(36)", maxLength: 36, nullable: true);
        migrationBuilder.AddColumn<string>("LastIndexedRetrievalVersionId", "Sources", type: "varchar(36)", maxLength: 36, nullable: true);
        migrationBuilder.AddColumn<string>("RetrievalVersionId", "ChatRequests", type: "varchar(36)", maxLength: 36, nullable: true);

        migrationBuilder.CreateTable(
            name: "RetrievalPresets",
            columns: table => new
            {
                Id = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false),
                Key = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                Name = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                Description = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true),
                ChunkSize = table.Column<int>(type: "int", nullable: false),
                ChunkOverlap = table.Column<int>(type: "int", nullable: false),
                EmbeddingModel = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                EmbeddingDimensions = table.Column<int>(type: "int", nullable: false),
                DefaultSearchMode = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                DefaultTopK = table.Column<int>(type: "int", nullable: false),
                DefaultHybridAlpha = table.Column<double>(type: "double", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "datetime", nullable: false),
            },
            constraints: table => table.PrimaryKey("PK_RetrievalPresets", x => x.Id));

        migrationBuilder.CreateTable(
            name: "NotebookRetrievalVersions",
            columns: table => new
            {
                Id = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false),
                NotebookId = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false),
                CreatedByUserId = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false),
                ParentVersionId = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: true),
                OriginPresetId = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: true),
                ChunkSize = table.Column<int>(type: "int", nullable: false),
                ChunkOverlap = table.Column<int>(type: "int", nullable: false),
                EmbeddingModel = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                EmbeddingDimensions = table.Column<int>(type: "int", nullable: false),
                DefaultSearchMode = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                DefaultTopK = table.Column<int>(type: "int", nullable: false),
                DefaultHybridAlpha = table.Column<double>(type: "double", nullable: false),
                Notes = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true),
                CreatedAt = table.Column<DateTime>(type: "datetime", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_NotebookRetrievalVersions", x => x.Id);
                table.ForeignKey("FK_NotebookRetrievalVersions_Notebooks_NotebookId", x => x.NotebookId, "Notebooks", "Id", onDelete: ReferentialAction.Cascade);
                table.ForeignKey("FK_NotebookRetrievalVersions_Users_CreatedByUserId", x => x.CreatedByUserId, "Users", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex("IX_RetrievalPresets_Key", "RetrievalPresets", "Key", unique: true);
        migrationBuilder.CreateIndex("IX_NotebookRetrievalVersions_NotebookId", "NotebookRetrievalVersions", "NotebookId");
        migrationBuilder.CreateIndex("IX_NotebookRetrievalVersions_ParentVersionId", "NotebookRetrievalVersions", "ParentVersionId");
        migrationBuilder.CreateIndex("IX_NotebookRetrievalVersions_OriginPresetId", "NotebookRetrievalVersions", "OriginPresetId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("NotebookRetrievalVersions");
        migrationBuilder.DropTable("RetrievalPresets");
        migrationBuilder.DropColumn("IsDevAdmin", "Users");
        migrationBuilder.DropColumn("ActiveRetrievalVersionId", "Notebooks");
        migrationBuilder.DropColumn("ActiveRetrievalVersionId", "Sources");
        migrationBuilder.DropColumn("LastIndexedRetrievalVersionId", "Sources");
        migrationBuilder.DropColumn("RetrievalVersionId", "ChatRequests");
    }
}
