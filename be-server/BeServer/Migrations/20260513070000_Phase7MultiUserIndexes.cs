using BeServer.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeServer.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260513070000_Phase7MultiUserIndexes")]
    public partial class Phase7MultiUserIndexes : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex("IX_Sources_UserId", "Sources", "UserId");
            migrationBuilder.CreateIndex("IX_Sources_UserId_NotebookId", "Sources", new[] { "UserId", "NotebookId" });
            migrationBuilder.CreateIndex("IX_Notes_UserId", "Notes", "UserId");
            migrationBuilder.CreateIndex("IX_Notes_UserId_NotebookId", "Notes", new[] { "UserId", "NotebookId" });
            migrationBuilder.CreateIndex("IX_ChatSessions_UserId", "ChatSessions", "UserId");
            migrationBuilder.CreateIndex("IX_ChatSessions_UserId_NotebookId", "ChatSessions", new[] { "UserId", "NotebookId" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex("IX_ChatSessions_UserId_NotebookId", "ChatSessions");
            migrationBuilder.DropIndex("IX_ChatSessions_UserId", "ChatSessions");
            migrationBuilder.DropIndex("IX_Notes_UserId_NotebookId", "Notes");
            migrationBuilder.DropIndex("IX_Notes_UserId", "Notes");
            migrationBuilder.DropIndex("IX_Sources_UserId_NotebookId", "Sources");
            migrationBuilder.DropIndex("IX_Sources_UserId", "Sources");
        }
    }
}
