using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebWritingTool.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGeminiQuotaControl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "InputTokens",
                table: "AiGenerationLogs",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OutputTokens",
                table: "AiGenerationLogs",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "GeminiQuotaStates",
                columns: table => new
                {
                    Scope = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    StateJson = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GeminiQuotaStates", x => new { x.Scope, x.Model });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GeminiQuotaStates");

            migrationBuilder.DropColumn(
                name: "InputTokens",
                table: "AiGenerationLogs");

            migrationBuilder.DropColumn(
                name: "OutputTokens",
                table: "AiGenerationLogs");
        }
    }
}
