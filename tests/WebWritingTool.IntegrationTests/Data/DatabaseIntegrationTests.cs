using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using WebWritingTool.Application.Admin;
using WebWritingTool.Application.Security;
using WebWritingTool.Domain.Articles;
using WebWritingTool.Infrastructure.Data;
using WebWritingTool.IntegrationTests.Support;

namespace WebWritingTool.IntegrationTests.Data;

[Collection(IntegrationTestCollection.Name)]
public class DatabaseIntegrationTests(IntegrationTestFixture fixture)
{
    [Fact]
    public async Task Migrations_AreAppliedToPostgreSqlDatabase()
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY "MigrationId";""",
            connection);

        var migrations = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            migrations.Add(reader.GetString(0));
        }

        Assert.Contains(migrations, migration => migration.EndsWith("_InitialIdentity", StringComparison.Ordinal));
        Assert.Contains(migrations, migration => migration.EndsWith("_AddBusinessDatabaseFoundation", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ModelIndexes_AreCreatedInPostgreSql()
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT indexname, indexdef
            FROM pg_indexes
            WHERE schemaname = 'public';
            """,
            connection);

        var indexes = new Dictionary<string, string>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            indexes[reader.GetString(0)] = reader.GetString(1);
        }

        Assert.Contains("IX_Articles_UserId_CreatedAt", indexes.Keys);
        Assert.Contains("IX_Articles_Tags_Gin", indexes.Keys);
        Assert.Contains("UX_XSearchPosts_PostId", indexes.Keys);
        Assert.Contains("UX_NotificationSettings_UserId_Provider_Active", indexes.Keys);
        Assert.Contains("USING gin", indexes["IX_Articles_Tags_Gin"], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ArticleHeadingLevelConstraint_RejectsH3WithoutParent()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var userId = $"constraint-user-{suffix}";
        await fixture.SeedUserAsync(userId, $"{userId}@example.test", ApplicationRoles.User);
        var articleId = await fixture.SeedArticleAsync(userId, $"constraint-keyword-{suffix}");

        using var scope = fixture.Factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.ArticleHeadings.Add(new ArticleHeading
        {
            ArticleId = articleId,
            ParentId = null,
            Level = 3,
            Title = "Invalid H3",
            DisplayOrder = 1,
            Status = HeadingStatus.Pending
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync());
    }

    [Fact]
    public async Task AdminDeleteUser_PhysicallyRemovesUserAndOwnedArticles()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var adminUserId = $"delete-admin-{suffix}";
        var targetUserId = $"delete-target-{suffix}";
        await fixture.SeedUserAsync(adminUserId, $"{adminUserId}@example.test", ApplicationRoles.Admin);
        await fixture.SeedUserAsync(targetUserId, $"{targetUserId}@example.test", ApplicationRoles.User);
        var articleId = await fixture.SeedArticleAsync(targetUserId, $"delete-keyword-{suffix}");

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var adminUserService = scope.ServiceProvider.GetRequiredService<IAdminUserService>();
            var result = await adminUserService.DeleteUserAsync(
                new AdminUserActor(adminUserId),
                targetUserId);

            Assert.True(result.Succeeded, result.Error.ToString());
        }

        using var verificationScope = fixture.Factory.Services.CreateScope();
        var dbContext = verificationScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.False(await dbContext.Users.AnyAsync(user => user.Id == targetUserId));
        Assert.False(await dbContext.Articles.IgnoreQueryFilters().AnyAsync(article => article.Id == articleId));
    }

    [Fact]
    public async Task AdminCreateThenDeleteUser_InSameScope_PhysicallyRemovesUser()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var adminUserId = $"delete-same-scope-admin-{suffix}";
        var targetEmail = $"delete-same-scope-target-{suffix}@example.test";
        await fixture.SeedUserAsync(adminUserId, $"{adminUserId}@example.test", ApplicationRoles.Admin);

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var adminUserService = scope.ServiceProvider.GetRequiredService<IAdminUserService>();
            var createResult = await adminUserService.CreateUserAsync(
                new CreateAdminUserCommand(
                    new AdminUserActor(adminUserId),
                    targetEmail,
                    "Delete Same Scope",
                    "Change-this-same-scope-123!",
                    ApplicationRoles.User,
                    IsEnabled: true,
                    MonthlyLimitChars: 200000,
                    RemainingOutlineCount: 40));

            Assert.True(createResult.Succeeded, createResult.Error.ToString());
            Assert.NotNull(createResult.Value);

            var deleteResult = await adminUserService.DeleteUserAsync(
                new AdminUserActor(adminUserId),
                createResult.Value.Id);

            Assert.True(deleteResult.Succeeded, deleteResult.Error.ToString());
        }

        using var verificationScope = fixture.Factory.Services.CreateScope();
        var dbContext = verificationScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.False(await dbContext.Users.AnyAsync(user => user.Email == targetEmail));
    }
}
