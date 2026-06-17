using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeServer.Migrations
{
    /// <inheritdoc />
    public partial class Phase19GraphExtractionStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GraphExtractionStatus",
                table: "ReindexJobs",
                type: "varchar(16)",
                maxLength: 16,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "GraphExtractionStatus",
                table: "IngestionJobs",
                type: "varchar(16)",
                maxLength: 16,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GraphExtractionStatus",
                table: "ReindexJobs");

            migrationBuilder.DropColumn(
                name: "GraphExtractionStatus",
                table: "IngestionJobs");
        }
    }
}
