using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using WebWritingTool.Domain.Articles;
using WebWritingTool.Domain.Jobs;
using WebWritingTool.Domain.Search;
using WebWritingTool.Infrastructure.Data;
using WebWritingTool.Infrastructure.Identity;
using WebWritingTool.IntegrationTests.Support;

namespace WebWritingTool.IntegrationTests.Data;

[Collection(IntegrationTestCollection.Name)]
public class ManualResearchMigrationTests(IntegrationTestFixture fixture)
{
    [Fact]
    public async Task Migration_RestoresOnlySourcesProvenBySuccessfulManualJobs()
    {
        // 履歴を戻すのは専用テストDBだけとし、コレクション共用DBには触れない。
        var databaseName = "manual_migration_" + Guid.NewGuid().ToString("N");
        await using (var connection = new NpgsqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"CREATE DATABASE {databaseName}", connection);
            await command.ExecuteNonQueryAsync();
        }
        var connectionString = new NpgsqlConnectionStringBuilder(fixture.ConnectionString) { Database = databaseName }.ConnectionString;
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connectionString).Options;
        await using var db = new ApplicationDbContext(options);
        await db.Database.MigrateAsync();
        var user = new ApplicationUser { Id = Guid.NewGuid().ToString("N") };
        var otherUser = new ApplicationUser { Id = Guid.NewGuid().ToString("N") };
        var article = new Article { UserId = user.Id, Keyword = "家庭菜園" };
        var otherArticle = new Article { UserId = user.Id, Keyword = "別の記事" };
        var heading = new ArticleHeading { ArticleId = article.Id, Level = 2, Title = "育て方", DisplayOrder = 1 };
        db.Users.AddRange(user, otherUser);
        db.Articles.AddRange(article, otherArticle);
        db.ArticleHeadings.Add(heading);
        var now = DateTimeOffset.UtcNow;
        var expected = new Dictionary<Guid, bool>();

        (SearchResult Source, ArticleGenerationJob Job) AddCase(string query, bool isManual)
        {
            var source = new SearchResult
            {
                UserId = user.Id,
                ArticleId = article.Id,
                Query = query,
                QueryHash = query,
                Url = $"https://example.test/{query}",
                Snippet = "保存済みの資料",
                FetchedAt = now.AddMinutes(-2),
                CacheExpiresAt = now.AddHours(1),
                ContentExpiresAt = now.AddHours(1)
            };
            var job = new ArticleGenerationJob
            {
                UserId = user.Id,
                ArticleId = article.Id,
                JobType = JobType.WebSearch,
                Status = JobStatus.Succeeded,
                QueuedAt = now.AddMinutes(-3),
                FinishedAt = now.AddMinutes(-1),
                PayloadJson = "{}",
                ResultJson = JsonSerializer.Serialize(new { queryHash = query })
            };
            db.SearchResults.Add(source);
            db.ArticleGenerationJobs.Add(job);
            expected.Add(source.Id, isManual);
            return (source, job);
        }

        AddCase("legacy-manual", true);
        var scoped = AddCase("heading-manual", true);
        scoped.Source.HeadingId = heading.Id;
        scoped.Job.HeadingId = heading.Id;
        var dummy = AddCase("dummy-manual", true);
        dummy.Source.IsDummy = true;
        dummy.Job.PayloadJson = """{"isDummy":true}""";
        var pascalCase = AddCase("legacy-payload-casing", true);
        pascalCase.Source.IsDummy = true;
        pascalCase.Job.PayloadJson = """{"IsDummy":true}""";
        AddCase("mode-mismatch", false).Job.PayloadJson = """{"isDummy":true}""";
        AddCase("automatic-refetch-after-manual-job", false).Source.FetchedAt = now;
        AddCase("failed-job", false).Job.Status = JobStatus.Failed;
        AddCase("automatic-job", false).Job.PayloadJson = """{"isManual":false}""";
        AddCase("other-heading", false).Job.HeadingId = heading.Id;
        AddCase("other-article", false).Job.ArticleId = otherArticle.Id;
        AddCase("other-owner", false).Job.UserId = otherUser.Id;
        AddCase("different-query", false).Job.ResultJson = """{"queryHash":"unrelated"}""";
        AddCase("missing-history", false).Job.ResultJson = null;
        await db.SaveChangesAsync();

        await db.GetService<IMigrator>().MigrateAsync("20260918001347_AddResearchDataIsolation");
        db.ChangeTracker.Clear();
        await db.Database.MigrateAsync();

        var results = await db.SearchResults.AsNoTracking().ToListAsync();
        Assert.Equal(expected.Count, results.Count);
        Assert.All(results, result =>
        {
            Assert.Equal(expected[result.Id], result.IsManual);
            Assert.Equal("保存済みの資料", result.Snippet);
        });
    }
}
