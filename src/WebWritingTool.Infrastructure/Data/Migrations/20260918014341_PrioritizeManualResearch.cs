using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebWritingTool.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class PrioritizeManualResearch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsManual",
                table: "SearchResults",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // 専用のWeb検索ジョブは手動操作でのみ登録されていたため、成功履歴から取得元を復元する。
            migrationBuilder.Sql("""
                UPDATE "SearchResults" AS result
                SET "IsManual" = TRUE
                WHERE EXISTS (
                    SELECT 1 FROM "ArticleGenerationJobs" AS job
                    WHERE job."JobType" = 'WebSearch'
                        AND job."Status" = 'Succeeded'
                        AND job."UserId" = result."UserId"
                        AND job."ArticleId" = result."ArticleId"
                        AND job."HeadingId" IS NOT DISTINCT FROM result."HeadingId"
                        AND job."ResultJson" ->> 'queryHash' = result."QueryHash"
                        AND (COALESCE(job."PayloadJson" ->> 'isDummy', job."PayloadJson" ->> 'IsDummy', 'false') = 'true') = result."IsDummy"
                        AND COALESCE(job."PayloadJson" ->> 'isManual', job."PayloadJson" ->> 'IsManual', 'true') = 'true'
                        AND result."FetchedAt" <= job."FinishedAt"
                );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsManual",
                table: "SearchResults");
        }
    }
}
