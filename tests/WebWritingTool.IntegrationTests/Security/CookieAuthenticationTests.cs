using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using WebWritingTool.Application.Articles;
using WebWritingTool.Application.Security;
using WebWritingTool.IntegrationTests.Support;

namespace WebWritingTool.IntegrationTests.Security;

[Collection(IntegrationTestCollection.Name)]
public class CookieAuthenticationTests(IntegrationTestFixture fixture)
{
    [Theory]
    [InlineData("/api/articles")]
    [InlineData("/api/articles/?page=1")]
    [InlineData("/API/articles")]
    [InlineData("/api/admin/users")]
    [InlineData("/api/security/antiforgery-token")]
    [InlineData("/api/notifications/settings")]
    public async Task ApiGet_WithoutCookie_ReturnsUnauthorizedProblemWithoutRedirect(string path)
    {
        using var factory = CreateCookieFactory();
        using var client = CreateClient(factory);

        using var response = await client.GetAsync(path);

        await AssertProblemAsync(response, HttpStatusCode.Unauthorized, "Unauthorized");
    }

    [Theory]
    [InlineData("POST", "/api/articles")]
    [InlineData("PUT", "/api/articles/00000000-0000-0000-0000-000000000000")]
    [InlineData("DELETE", "/api/articles/00000000-0000-0000-0000-000000000000")]
    public async Task ApiMutation_WithoutCookie_ReturnsUnauthorizedBeforeCsrfValidation(string method, string path)
    {
        using var factory = CreateCookieFactory();
        using var client = CreateClient(factory);
        using var request = new HttpRequestMessage(new HttpMethod(method), path)
        {
            Content = JsonContent.Create(new { })
        };

        using var response = await client.SendAsync(request);

        await AssertProblemAsync(response, HttpStatusCode.Unauthorized, "Unauthorized");
    }

    [Theory]
    [InlineData("/articles")]
    [InlineData("/admin/users")]
    public async Task ProtectedPage_WithoutCookie_RedirectsToLogin(string path)
    {
        using var factory = CreateCookieFactory();
        using var client = CreateClient(factory);

        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        Assert.Equal("/login", response.Headers.Location.AbsolutePath);
        Assert.Equal($"?ReturnUrl={path}", Uri.UnescapeDataString(response.Headers.Location.Query));
    }

    [Fact]
    public async Task NormalUserCookie_AllowsOwnedArticles_RejectsAdminApi_AndPreservesPageRedirect()
    {
        var userId = $"cookie-auth-{Guid.NewGuid():N}";
        var email = $"{userId}@example.test";
        const string password = "Cookie-auth-test-123!";
        await fixture.SeedUserWithPasswordAsync(userId, email, password, ApplicationRoles.User);
        var articleId = await fixture.SeedArticleAsync(userId, "Cookie authentication regression");
        using var factory = CreateCookieFactory();
        using var client = CreateClient(factory);
        await SetCsrfTokenAsync(client);

        using var login = await client.PostAsync("/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["email"] = email,
            ["password"] = password,
            ["returnUrl"] = "/articles"
        }));

        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Assert.Equal("/articles", login.Headers.Location?.OriginalString);
        var articles = await client.GetFromJsonAsync<ArticleListResponse>("/api/articles");
        Assert.NotNull(articles);
        Assert.Contains(articles.Items, article => article.Id == articleId);

        using var adminApi = await client.GetAsync("/api/admin/users");
        await AssertProblemAsync(adminApi, HttpStatusCode.Forbidden, "Forbidden");

        using var adminPage = await client.GetAsync("/admin/users");
        Assert.Equal(HttpStatusCode.Redirect, adminPage.StatusCode);
        Assert.NotNull(adminPage.Headers.Location);
        Assert.Equal("/forbidden", adminPage.Headers.Location.AbsolutePath);

        await SetCsrfTokenAsync(client);
        using var logout = await client.PostAsync("/logout", null);
        Assert.Equal(HttpStatusCode.Redirect, logout.StatusCode);
        using var afterLogout = await client.GetAsync("/api/articles");
        await AssertProblemAsync(afterLogout, HttpStatusCode.Unauthorized, "Unauthorized");
    }

    private WebApplicationFactory<Program> CreateCookieFactory() => fixture.Factory.WithWebHostBuilder(builder =>
        builder.ConfigureTestServices(services => services.PostConfigure<AuthenticationOptions>(options =>
        {
            // 認証スタブでは本番Cookieのchallenge/forbidによるリダイレクトを検証できない。
            options.DefaultAuthenticateScheme = IdentityConstants.ApplicationScheme;
            options.DefaultChallengeScheme = IdentityConstants.ApplicationScheme;
            options.DefaultForbidScheme = IdentityConstants.ApplicationScheme;
        })));

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory) => factory.CreateClient(
        new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });

    private static async Task SetCsrfTokenAsync(HttpClient client)
    {
        var html = await client.GetStringAsync("/login");
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(match.Success);
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", WebUtility.HtmlDecode(match.Groups[1].Value));
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string title)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal((int)status, problem.Status);
        Assert.Equal(title, problem.Title);
    }
}
