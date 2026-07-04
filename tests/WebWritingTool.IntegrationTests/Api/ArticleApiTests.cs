using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WebWritingTool.Application.Articles;
using WebWritingTool.Application.Security;
using WebWritingTool.Infrastructure.Data;
using WebWritingTool.IntegrationTests.Support;

namespace WebWritingTool.IntegrationTests.Api;

[Collection(IntegrationTestCollection.Name)]
public class ArticleApiTests(IntegrationTestFixture fixture)
{
    [Fact]
    public async Task GetArticles_WithoutAuthentication_ReturnsUnauthorized()
    {
        using var client = fixture.CreateAnonymousClient();

        var response = await client.GetAsync("/api/articles");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateAndListArticles_AsOwner_ReturnsOnlyOwnedArticles()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var ownerUserId = $"api-owner-{suffix}";
        var otherUserId = $"api-other-{suffix}";
        await fixture.SeedUserAsync(ownerUserId, $"{ownerUserId}@example.test", ApplicationRoles.User);
        await fixture.SeedUserAsync(otherUserId, $"{otherUserId}@example.test", ApplicationRoles.User);
        var otherArticleId = await fixture.SeedArticleAsync(otherUserId, $"other-keyword-{suffix}");

        using var client = await fixture.CreateAuthenticatedClientAsync(ownerUserId, ApplicationRoles.User);
        var createResponse = await client.PostAsJsonAsync("/api/articles", new
        {
            keyword = $"owner-keyword-{suffix}",
            title = "Owner article",
            generateImage = false,
            h2Count = 2,
            h3Count = 1,
            tone = "calm",
            tags = new[] { "SEO", "test" },
            memo = "memo",
            suggestedKeywords = (string?)null,
            relatedKeywords = (string?)null,
            learningType = "None",
            learningText = (string?)null,
            additionalPrompt = (string?)null,
            writingProfileWordpressSiteId = (Guid?)null,
            outlineMethod = "Keyword",
            generationModel = "gemini-3.5-flash",
            searchMode = false,
            isDomesticOnly = true,
            notificationMode = "None"
        });

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<CreateArticleResponse>();
        Assert.NotNull(created);

        var list = await client.GetFromJsonAsync<ArticleListResponse>("/api/articles?page=1&pageSize=20");

        Assert.NotNull(list);
        Assert.Contains(list.Items, item => item.Id == created.Id && item.Keyword == $"owner-keyword-{suffix}");
        Assert.DoesNotContain(list.Items, item => item.Id == otherArticleId);
    }

    [Fact]
    public async Task CompleteHumanReview_AsOwner_PersistsReviewerAndAuditLog()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var userId = $"review-owner-{suffix}";
        await fixture.SeedUserAsync(userId, $"{userId}@example.test", ApplicationRoles.User);
        var articleId = await fixture.SeedArticleAsync(userId, $"review-keyword-{suffix}");
        using var client = await fixture.CreateAuthenticatedClientAsync(userId, ApplicationRoles.User);

        var response = await client.PostAsJsonAsync(
            $"/api/articles/{articleId}/human-review",
            new { reviewComment = "内容を確認済み", rowVersion = (string?)null });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<HumanReviewResponse>();
        Assert.NotNull(result);
        Assert.Equal(articleId, result.ArticleId);
        Assert.Equal(userId, result.HumanReviewedByUserId);
        Assert.NotNull(result.HumanReviewedAt);

        using var scope = fixture.Factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var article = await dbContext.Articles.AsNoTracking().SingleAsync(item => item.Id == articleId);
        Assert.Equal(userId, article.HumanReviewedByUserId);
        Assert.NotNull(article.HumanReviewedAt);
        Assert.True(await dbContext.AuditLogs.AnyAsync(log =>
            log.UserId == userId
            && log.EntityId == articleId.ToString()
            && log.Action == "HumanReviewCompleted"));
    }

    [Fact]
    public async Task CompleteHumanReviewAsync_ForOtherUsersArticle_ReturnsNotFound()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var ownerUserId = $"review-owner-{suffix}";
        var otherUserId = $"review-other-{suffix}";
        await fixture.SeedUserAsync(ownerUserId, $"{ownerUserId}@example.test", ApplicationRoles.User);
        await fixture.SeedUserAsync(otherUserId, $"{otherUserId}@example.test", ApplicationRoles.User);
        var articleId = await fixture.SeedArticleAsync(ownerUserId, $"review-keyword-{suffix}");

        using var scope = fixture.Factory.Services.CreateScope();
        var reviewService = scope.ServiceProvider.GetRequiredService<IArticleReviewService>();
        var result = await reviewService.CompleteHumanReviewAsync(
            new CompleteHumanReviewCommand(
                new ArticleActor(otherUserId, IsAdmin: false),
                articleId,
                "ownership check",
                null));

        Assert.False(result.Succeeded);
        Assert.Equal(ArticleServiceError.NotFound, result.Error);
    }

    [Fact]
    public async Task CompleteHumanReview_WithStaleRowVersion_ReturnsConflict()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var userId = $"review-conflict-{suffix}";
        await fixture.SeedUserAsync(userId, $"{userId}@example.test", ApplicationRoles.User);
        var articleId = await fixture.SeedArticleAsync(userId, $"review-keyword-{suffix}");
        using var client = await fixture.CreateAuthenticatedClientAsync(userId, ApplicationRoles.User);
        var detail = await client.GetFromJsonAsync<ArticleDetailResponse>($"/api/articles/{articleId}");
        Assert.NotNull(detail);

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var article = await dbContext.Articles.SingleAsync(item => item.Id == articleId);
            article.Memo = "concurrent update";
            await dbContext.SaveChangesAsync();
        }

        var response = await client.PostAsJsonAsync(
            $"/api/articles/{articleId}/human-review",
            new { reviewComment = (string?)null, rowVersion = detail.RowVersion });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }
}
