using System;
using BeServer.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeServer.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260516010000_Phase13DbIntegrity")]
public partial class Phase13DbIntegrity : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<DateTime>(
            name: "AvailableAt",
            table: "IngestionJobs",
            type: "datetime",
            nullable: false,
            defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified),
            oldClrType: typeof(DateTime),
            oldType: "datetime",
            oldNullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_RequestLogs_CreatedAt",
            table: "RequestLogs",
            column: "CreatedAt");

        migrationBuilder.CreateIndex(
            name: "IX_SessionTasks_CreatedFromRequestId",
            table: "SessionTasks",
            column: "CreatedFromRequestId");

        migrationBuilder.CreateIndex(
            name: "IX_SessionTasks_UpdatedFromRequestId",
            table: "SessionTasks",
            column: "UpdatedFromRequestId");

        migrationBuilder.CreateIndex(
            name: "IX_ChatMessages_RequestId",
            table: "ChatMessages",
            column: "RequestId");

        migrationBuilder.CreateIndex(
            name: "IX_ChatSessions_ActiveTaskId",
            table: "ChatSessions",
            column: "ActiveTaskId");

        migrationBuilder.AddForeignKey(
            name: "FK_ChatMessages_ChatRequests_RequestId",
            table: "ChatMessages",
            column: "RequestId",
            principalTable: "ChatRequests",
            principalColumn: "Id",
            onDelete: ReferentialAction.SetNull);

        migrationBuilder.AddForeignKey(
            name: "FK_ChatSessions_SessionTasks_ActiveTaskId",
            table: "ChatSessions",
            column: "ActiveTaskId",
            principalTable: "SessionTasks",
            principalColumn: "Id",
            onDelete: ReferentialAction.SetNull);

        migrationBuilder.AddForeignKey(
            name: "FK_SessionTasks_ChatRequests_CreatedFromRequestId",
            table: "SessionTasks",
            column: "CreatedFromRequestId",
            principalTable: "ChatRequests",
            principalColumn: "Id",
            onDelete: ReferentialAction.SetNull);

        migrationBuilder.AddForeignKey(
            name: "FK_SessionTasks_ChatRequests_UpdatedFromRequestId",
            table: "SessionTasks",
            column: "UpdatedFromRequestId",
            principalTable: "ChatRequests",
            principalColumn: "Id",
            onDelete: ReferentialAction.SetNull);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_ChatMessages_ChatRequests_RequestId",
            table: "ChatMessages");

        migrationBuilder.DropForeignKey(
            name: "FK_ChatSessions_SessionTasks_ActiveTaskId",
            table: "ChatSessions");

        migrationBuilder.DropForeignKey(
            name: "FK_SessionTasks_ChatRequests_CreatedFromRequestId",
            table: "SessionTasks");

        migrationBuilder.DropForeignKey(
            name: "FK_SessionTasks_ChatRequests_UpdatedFromRequestId",
            table: "SessionTasks");

        migrationBuilder.DropIndex(
            name: "IX_RequestLogs_CreatedAt",
            table: "RequestLogs");

        migrationBuilder.DropIndex(
            name: "IX_SessionTasks_CreatedFromRequestId",
            table: "SessionTasks");

        migrationBuilder.DropIndex(
            name: "IX_SessionTasks_UpdatedFromRequestId",
            table: "SessionTasks");

        migrationBuilder.DropIndex(
            name: "IX_ChatMessages_RequestId",
            table: "ChatMessages");

        migrationBuilder.DropIndex(
            name: "IX_ChatSessions_ActiveTaskId",
            table: "ChatSessions");

        migrationBuilder.AlterColumn<DateTime>(
            name: "AvailableAt",
            table: "IngestionJobs",
            type: "datetime",
            nullable: true,
            oldClrType: typeof(DateTime),
            oldType: "datetime");
    }
}
