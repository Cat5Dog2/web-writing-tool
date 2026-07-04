using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WebWritingTool.Application.Search;
using WebWritingTool.Application.Security;
using WebWritingTool.Domain.Search;
using WebWritingTool.Infrastructure.Data;
using WebWritingTool.Infrastructure.Search;
using WebWritingTool.IntegrationTests.Support;

namespace WebWritingTool.IntegrationTests.Jobs;

[Collection(IntegrationTestCollection.Name)]
public class XPostRehydrationIntegrationTests(IntegrationTestFixture fixture)
{
    [Fact]
    public async Task RehydrateCachedPostsAsync_WithMoreThanOneHundredPosts_UsesBatches()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var userId = $"rehydration-user-{suffix}";
        await fixture.SeedUserAsync(userId, $"{userId}@example.test", ApplicationRoles.User);
        var articleId = await fixture.SeedArticleAsync(userId, $"rehydration-keyword-{suffix}");
        var postIds = Enumerable.Range(1, 101)
            .Select(index => $"{index:D10}")
            .ToArray();

        using var scope = fixture.Factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var fetchedAt = DateTimeOffset.UtcNow.AddHours(-2);
        dbContext.XSearchPosts.AddRange(postIds.Select(postId => new XSearchPost
        {
            UserId = userId,
            ArticleId = articleId,
            Query = "test",
            QueryHash = $"hash-{postId}",
            PostId = postId,
            Text = $"cached-{postId}",
            FetchedAt = fetchedAt
        }));
        await dbContext.SaveChangesAsync();

        var xClient = new CappedXClient();
        var service = new XPostRehydrationService(
            dbContext,
            xClient,
            new SearchCachePolicyResolver(new SearchCachePolicyOptions
            {
                Policy = SearchCachePolicies.Strict
            }));

        var result = await service.RehydrateCachedPostsAsync(
            userId,
            postIds,
            TopicRiskMode.Strict);

        Assert.Equal(2, xClient.Requests.Count);
        Assert.All(xClient.Requests, request => Assert.InRange(request.Count, 1, 100));
        Assert.Equal(101, result.RefreshedCount);
        Assert.Equal(0, result.MissingCount);
        Assert.Equal(
            101,
            await dbContext.XSearchPosts.CountAsync(post => post.ArticleId == articleId && post.Text != null));
    }

    [Fact]
    public async Task RehydrateCachedPostsAsync_WhenPostContentChanged_ReportsChangedPost()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var userId = $"rehydration-change-user-{suffix}";
        await fixture.SeedUserAsync(userId, $"{userId}@example.test", ApplicationRoles.User);
        var articleId = await fixture.SeedArticleAsync(userId, $"rehydration-change-keyword-{suffix}");
        const string postId = "1234567890";

        using var scope = fixture.Factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.XSearchPosts.Add(new XSearchPost
        {
            UserId = userId,
            ArticleId = articleId,
            Query = "test",
            QueryHash = $"hash-{suffix}",
            PostId = postId,
            Text = "cached-content",
            AuthorId = $"author-{postId}",
            Url = $"https://x.com/example/status/{postId}",
            Language = "ja",
            PostedAt = DateTimeOffset.UnixEpoch,
            FetchedAt = DateTimeOffset.UtcNow.AddHours(-2)
        });
        await dbContext.SaveChangesAsync();

        var service = new XPostRehydrationService(
            dbContext,
            new CappedXClient(),
            new SearchCachePolicyResolver(new SearchCachePolicyOptions
            {
                Policy = SearchCachePolicies.Strict
            }));

        var result = await service.RehydrateCachedPostsAsync(
            userId,
            [postId],
            TopicRiskMode.Strict);

        Assert.Equal(1, result.ChangedCount);
    }

    private sealed class CappedXClient : IXFullArchiveSearchClient
    {
        public List<IReadOnlyList<string>> Requests { get; } = [];

        public Task<IReadOnlyList<XSearchPostResult>> SearchAsync(
            XFullArchiveSearchRequest request,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<XSearchPostResult>> RehydrateAsync(
            XPostRehydrationRequest request,
            CancellationToken cancellationToken = default)
        {
            var acceptedIds = request.PostIds.Take(100).ToArray();
            Requests.Add(acceptedIds);
            IReadOnlyList<XSearchPostResult> results = acceptedIds
                .Select(postId => new XSearchPostResult(
                    postId,
                    $"author-{postId}",
                    $"fresh-{postId}",
                    $"https://x.com/example/status/{postId}",
                    "ja",
                    DateTimeOffset.UtcNow))
                .ToArray();
            return Task.FromResult(results);
        }
    }
}
