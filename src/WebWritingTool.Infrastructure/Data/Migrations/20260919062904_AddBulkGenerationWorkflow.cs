using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebWritingTool.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBulkGenerationWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "GenerationRunId",
                table: "ArticleGenerationJobs",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ArticleGenerationRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    ArticleId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    SettingsJson = table.Column<string>(type: "jsonb", nullable: false),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Stage = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    StopRequested = table.Column<bool>(type: "boolean", nullable: false),
                    Warning = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FinishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArticleGenerationRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ArticleGenerationRuns_Articles_ArticleId",
                        column: x => x.ArticleId,
                        principalTable: "Articles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ArticleGenerationRuns_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ArticleGenerationJobs_GenerationRunId_JobType",
                table: "ArticleGenerationJobs",
                columns: new[] { "GenerationRunId", "JobType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ArticleGenerationRuns_ArticleId",
                table: "ArticleGenerationRuns",
                column: "ArticleId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ArticleGenerationRuns_UserId_BatchId",
                table: "ArticleGenerationRuns",
                columns: new[] { "UserId", "BatchId" });

            migrationBuilder.AddForeignKey(
                name: "FK_ArticleGenerationJobs_ArticleGenerationRuns_GenerationRunId",
                table: "ArticleGenerationJobs",
                column: "GenerationRunId",
                principalTable: "ArticleGenerationRuns",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ArticleGenerationJobs_ArticleGenerationRuns_GenerationRunId",
                table: "ArticleGenerationJobs");

            migrationBuilder.DropTable(
                name: "ArticleGenerationRuns");

            migrationBuilder.DropIndex(
                name: "IX_ArticleGenerationJobs_GenerationRunId_JobType",
                table: "ArticleGenerationJobs");

            migrationBuilder.DropColumn(
                name: "GenerationRunId",
                table: "ArticleGenerationJobs");
        }
    }
}
