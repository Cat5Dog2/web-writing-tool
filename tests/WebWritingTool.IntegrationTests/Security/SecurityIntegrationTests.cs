using System.Net;
using System.Net.Http.Json;
using WebWritingTool.Application.Security;
using WebWritingTool.Application.Wordpress;
using WebWritingTool.IntegrationTests.Support;

namespace WebWritingTool.IntegrationTests.Security;

[Collection(IntegrationTestCollection.Name)]
public class SecurityIntegrationTests(IntegrationTestFixture fixture)
{
    [Fact]
    public async Task StateChangingApi_WithoutCsrfToken_ReturnsBadRequest()
    {
        using var client = fixture.CreateAnonymousClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, $"csrf-user-{Guid.NewGuid():N}");
        client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeader, ApplicationRoles.User);

        var response = await client.PostAsJsonAsync("/api/articles", new
        {
            keyword = "csrf keyword",
            title = "CSRF title",
            generateImage = false,
            h2Count = 2,
            h3Count = 1,
            tone = "calm",
            tags = Array.Empty<string>(),
            memo = (string?)null,
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

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("CSRF", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WordpressSiteResponses_DoNotExposeApplicationPassword()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var userId = $"wp-secret-user-{suffix}";
        const string applicationPassword = "wp-app-pass-secret-value";
        await fixture.SeedUserAsync(userId, $"{userId}@example.test", ApplicationRoles.User);

        using var client = await fixture.CreateAuthenticatedClientAsync(userId, ApplicationRoles.User);
        var createResponse = await client.PostAsJsonAsync("/api/wordpress-sites", new
        {
            siteName = "Secret Site",
            baseUrl = "https://example.test",
            loginId = "writer",
            applicationPassword,
            defaultCategoryId = (int?)null,
            defaultCategoryName = (string?)null,
            siteAdminProfile = "admin profile",
            writingCharacter = "writing character",
            readerPersona = "reader persona"
        });

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var createBody = await createResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain(applicationPassword, createBody, StringComparison.Ordinal);

        var created = await createResponse.Content.ReadFromJsonAsync<WordpressSiteResponse>();
        Assert.NotNull(created);

        var listResponse = await client.GetStringAsync("/api/wordpress-sites");
        Assert.Contains(created.Id.ToString(), listResponse, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(applicationPassword, listResponse, StringComparison.Ordinal);
    }
}
