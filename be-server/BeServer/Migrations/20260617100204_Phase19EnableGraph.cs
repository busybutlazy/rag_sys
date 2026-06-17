using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeServer.Migrations
{
    /// <inheritdoc />
    public partial class Phase19EnableGraph : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "EnableGraph",
                table: "NotebookRetrievalVersions",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "GraphExtractionModel",
                table: "NotebookRetrievalVersions",
                type: "varchar(128)",
                maxLength: 128,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "MaxFactHits",
                table: "NotebookRetrievalVersions",
                type: "int",
                nullable: false,
                defaultValue: 8);

            migrationBuilder.AddColumn<int>(
                name: "MaxGraphHops",
                table: "NotebookRetrievalVersions",
                type: "int",
                nullable: false,
                defaultValue: 1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EnableGraph",
                table: "NotebookRetrievalVersions");

            migrationBuilder.DropColumn(
                name: "GraphExtractionModel",
                table: "NotebookRetrievalVersions");

            migrationBuilder.DropColumn(
                name: "MaxFactHits",
                table: "NotebookRetrievalVersions");

            migrationBuilder.DropColumn(
                name: "MaxGraphHops",
                table: "NotebookRetrievalVersions");
        }
    }
}
