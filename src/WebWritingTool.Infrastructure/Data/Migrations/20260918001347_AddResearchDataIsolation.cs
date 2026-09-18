using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebWritingTool.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddResearchDataIsolation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_XSearchPosts_UserId",
                table: "XSearchPosts");

            migrationBuilder.DropIndex(
                name: "UX_XSearchPosts_PostId",
                table: "XSearchPosts");

            migrationBuilder.AddColumn<bool>(
                name: "IsDummy",
                table: "XSearchPosts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsDummy",
                table: "SearchResults",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "UX_XSearchPosts_Scope_PostId",
                table: "XSearchPosts",
                columns: new[] { "UserId", "ArticleId", "HeadingId", "IsDummy", "QueryHash", "PostId" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_XSearchPosts_Scope_PostId",
                table: "XSearchPosts");

            migrationBuilder.DropColumn(
                name: "IsDummy",
                table: "XSearchPosts");

            migrationBuilder.DropColumn(
                name: "IsDummy",
                table: "SearchResults");

            migrationBuilder.CreateIndex(
                name: "IX_XSearchPosts_UserId",
                table: "XSearchPosts",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "UX_XSearchPosts_PostId",
                table: "XSearchPosts",
                column: "PostId",
                unique: true);
        }
    }
}
