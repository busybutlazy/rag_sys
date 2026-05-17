using BeServer.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeServer.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260515010000_Phase10UploadSecurity")]
public partial class Phase10UploadSecurity : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "DetectedMimeType",
            table: "Sources",
            type: "varchar(128)",
            maxLength: 128,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "OriginalContentType",
            table: "Sources",
            type: "varchar(128)",
            maxLength: 128,
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "DetectedMimeType", table: "Sources");
        migrationBuilder.DropColumn(name: "OriginalContentType", table: "Sources");
    }
}
