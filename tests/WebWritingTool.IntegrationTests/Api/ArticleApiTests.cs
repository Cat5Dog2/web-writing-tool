using System.Net;
using System.Net.Http.Json;
using WebWritingTool.Application.Articles;
using WebWritingTool.Application.Security;
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
}
