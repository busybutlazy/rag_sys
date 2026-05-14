using System;
using BeServer.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeServer.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260513090000_ChatOrchestration")]
    public partial class ChatOrchestration : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ActiveTaskId",
                table: "ChatSessions",
                type: "varchar(36)",
                maxLength: 36,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Archived",
                table: "ChatSessions",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastMessageAt",
                table: "ChatSessions",
                type: "datetime",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Mode",
                table: "ChatSessions",
                type: "varchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "chat");

            migrationBuilder.AddColumn<string>(
                name: "SessionStateJson",
                table: "ChatSessions",
                type: "json",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ChatMessages",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false),
                    SessionId = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false),
                    UserId = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false),
                    NotebookId = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false),
                    Role = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false),
                    Content = table.Column<string>(type: "longtext", nullable: false),
                    ContentPreview = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    RequestId = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: true),
                    SourcesJson = table.Column<string>(type: "json", nullable: true),
                    TracesJson = table.Column<string>(type: "json", nullable: true),
                    MetadataJson = table.Column<string>(type: "json", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatMessages", x => x.Id);
                    table.ForeignKey("FK_ChatMessages_ChatSessions_SessionId", x => x.SessionId, "ChatSessions", "Id", onDelete: ReferentialAction.Cascade);
                    table.ForeignKey("FK_ChatMessages_Notebooks_NotebookId", x => x.NotebookId, "Notebooks", "Id", onDelete: ReferentialAction.Cascade);
                    table.ForeignKey("FK_ChatMessages_Users_UserId", x => x.UserId, "Users", "Id", onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ChatRequests",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false),
                    SessionId = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false),
                    UserMessageId = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: true),
                    AssistantMessageId = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: true),
                    Mode = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    Model = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                    Status = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    ContextSnapshotJson = table.Column<string>(type: "json", nullable: true),
                    Error = table.Column<string>(type: "text", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "datetime", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime", nullable: true),
                    DurationMs = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatRequests", x => x.Id);
                    table.ForeignKey("FK_ChatRequests_ChatSessions_SessionId", x => x.SessionId, "ChatSessions", "Id", onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SessionTasks",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false),
                    SessionId = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false),
                    Title = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: false),
                    Summary = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    StateJson = table.Column<string>(type: "json", nullable: true),
                    CreatedFromRequestId = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: true),
                    UpdatedFromRequestId = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SessionTasks", x => x.Id);
                    table.ForeignKey("FK_SessionTasks_ChatSessions_SessionId", x => x.SessionId, "ChatSessions", "Id", onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RequestLogs",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false),
                    ChatRequestId = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: true),
                    SessionId = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: true),
                    Direction = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    Service = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    Operation = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                    Method = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: true),
                    Url = table.Column<string>(type: "varchar(2048)", maxLength: 2048, nullable: true),
                    RequestJson = table.Column<string>(type: "json", nullable: true),
                    ResponseJson = table.Column<string>(type: "json", nullable: true),
                    StatusCode = table.Column<int>(type: "int", nullable: true),
                    DurationMs = table.Column<int>(type: "int", nullable: true),
                    Error = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RequestLogs", x => x.Id);
                    table.ForeignKey("FK_RequestLogs_ChatRequests_ChatRequestId", x => x.ChatRequestId, "ChatRequests", "Id", onDelete: ReferentialAction.SetNull);
                    table.ForeignKey("FK_RequestLogs_ChatSessions_SessionId", x => x.SessionId, "ChatSessions", "Id", onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex("IX_ChatMessages_NotebookId", "ChatMessages", "NotebookId");
            migrationBuilder.CreateIndex("IX_ChatMessages_SessionId", "ChatMessages", "SessionId");
            migrationBuilder.CreateIndex("IX_ChatMessages_SessionId_Sequence", "ChatMessages", new[] { "SessionId", "Sequence" }, unique: true);
            migrationBuilder.CreateIndex("IX_ChatMessages_UserId", "ChatMessages", "UserId");
            migrationBuilder.CreateIndex("IX_ChatMessages_UserId_NotebookId", "ChatMessages", new[] { "UserId", "NotebookId" });
            migrationBuilder.CreateIndex("IX_ChatRequests_SessionId", "ChatRequests", "SessionId");
            migrationBuilder.CreateIndex("IX_ChatRequests_UserMessageId", "ChatRequests", "UserMessageId");
            migrationBuilder.CreateIndex("IX_RequestLogs_ChatRequestId", "RequestLogs", "ChatRequestId");
            migrationBuilder.CreateIndex("IX_RequestLogs_Service_Operation", "RequestLogs", new[] { "Service", "Operation" });
            migrationBuilder.CreateIndex("IX_RequestLogs_SessionId", "RequestLogs", "SessionId");
            migrationBuilder.CreateIndex("IX_SessionTasks_SessionId", "SessionTasks", "SessionId");
            migrationBuilder.CreateIndex("IX_SessionTasks_SessionId_Status", "SessionTasks", new[] { "SessionId", "Status" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable("RequestLogs");
            migrationBuilder.DropTable("SessionTasks");
            migrationBuilder.DropTable("ChatMessages");
            migrationBuilder.DropTable("ChatRequests");
            migrationBuilder.DropColumn("ActiveTaskId", "ChatSessions");
            migrationBuilder.DropColumn("Archived", "ChatSessions");
            migrationBuilder.DropColumn("LastMessageAt", "ChatSessions");
            migrationBuilder.DropColumn("Mode", "ChatSessions");
            migrationBuilder.DropColumn("SessionStateJson", "ChatSessions");
        }
    }
}
