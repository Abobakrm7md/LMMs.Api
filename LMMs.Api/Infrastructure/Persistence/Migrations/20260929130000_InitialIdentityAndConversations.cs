using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LMMs.Api.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260929130000_InitialIdentityAndConversations")]
public partial class InitialIdentityAndConversations : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Users",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                NormalizedEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                UserName = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                NormalizedUserName = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                DisplayName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                PasswordHash = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", nullable: false),
                LastLoginAt = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", nullable: true),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_Users", x => x.Id));

        migrationBuilder.CreateTable(
            name: "Conversations",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", nullable: false),
                LastMessageAt = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", nullable: true),
                NextMessageSequence = table.Column<int>(type: "int", nullable: false),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Conversations", x => x.Id);
                table.CheckConstraint("CK_Conversations_NextMessageSequence", "[NextMessageSequence] >= 0");
                table.ForeignKey(
                    name: "FK_Conversations_Users_UserId",
                    column: x => x.UserId,
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "ChatMessages",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ConversationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                SequenceNumber = table.Column<int>(type: "int", nullable: false),
                Role = table.Column<byte>(type: "tinyint", nullable: false),
                Status = table.Column<byte>(type: "tinyint", nullable: false),
                Content = table.Column<string>(type: "nvarchar(max)", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", nullable: false),
                ModelName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ChatMessages", x => x.Id);
                table.CheckConstraint("CK_ChatMessages_SequenceNumber", "[SequenceNumber] > 0");
                table.ForeignKey(
                    name: "FK_ChatMessages_Conversations_ConversationId",
                    column: x => x.ConversationId,
                    principalTable: "Conversations",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "ToolExecutions",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                AssistantMessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                StepNumber = table.Column<int>(type: "int", nullable: false),
                ToolName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                ArgumentsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                Output = table.Column<string>(type: "nvarchar(max)", nullable: true),
                Error = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                Succeeded = table.Column<bool>(type: "bit", nullable: false),
                StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", nullable: false),
                CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ToolExecutions", x => x.Id);
                table.CheckConstraint("CK_ToolExecutions_StepNumber", "[StepNumber] > 0");
                table.ForeignKey(
                    name: "FK_ToolExecutions_ChatMessages_AssistantMessageId",
                    column: x => x.AssistantMessageId,
                    principalTable: "ChatMessages",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "UX_Users_NormalizedEmail",
            table: "Users",
            column: "NormalizedEmail",
            unique: true);
        migrationBuilder.CreateIndex(
            name: "UX_Users_NormalizedUserName",
            table: "Users",
            column: "NormalizedUserName",
            unique: true);
        migrationBuilder.CreateIndex(
            name: "IX_Conversations_UserId_UpdatedAt",
            table: "Conversations",
            columns: new[] { "UserId", "UpdatedAt" });
        migrationBuilder.CreateIndex(
            name: "UX_ChatMessages_ConversationId_SequenceNumber",
            table: "ChatMessages",
            columns: new[] { "ConversationId", "SequenceNumber" },
            unique: true);
        migrationBuilder.CreateIndex(
            name: "IX_ToolExecutions_AssistantMessageId_StepNumber",
            table: "ToolExecutions",
            columns: new[] { "AssistantMessageId", "StepNumber" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "ToolExecutions");
        migrationBuilder.DropTable(name: "ChatMessages");
        migrationBuilder.DropTable(name: "Conversations");
        migrationBuilder.DropTable(name: "Users");
    }
}
