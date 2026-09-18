using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using WebWritingTool.Application.Security;
using WebWritingTool.Infrastructure.Identity;
using WebWritingTool.Infrastructure.Data;
using WebWritingTool.IntegrationTests.Support;

namespace WebWritingTool.IntegrationTests.Api;

[Collection(IntegrationTestCollection.Name)]
public class GuestLoginTests(IntegrationTestFixture fixture)
{
    [Fact]
    public async Task GuestLogin_WithoutCredentials_CanOpenArticlesAndLogout()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });
        await SetCsrfTokenAsync(client);

        var response = await client.PostAsync("/login/guest", new FormUrlEncodedContent(
            new Dictionary<string, string> { ["returnUrl"] = "/articles" }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/articles", response.Headers.Location?.OriginalString);
        var ticket = ReadTicket(factory, response);
        Assert.True(GuestIdentity.IsGuest(ticket.Principal));
        Assert.True(ticket.Principal.IsInRole(ApplicationRoles.User));
        Assert.False(ticket.Principal.IsInRole(ApplicationRoles.Admin));
        Assert.False(ticket.Properties.IsPersistent);
        Assert.False(ticket.Properties.AllowRefresh);
        Assert.InRange(ticket.Properties.ExpiresUtc!.Value - DateTimeOffset.UtcNow,
            TimeSpan.FromHours(7.9), TimeSpan.FromHours(8));
        using (var scope = factory.Services.CreateScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var guest = await manager.GetUserAsync(ticket.Principal);
            Assert.NotNull(guest);
            Assert.Null(guest.Email);
            Assert.False(await manager.HasPasswordAsync(guest));
            Assert.Contains(await manager.GetClaimsAsync(guest), claim => claim.Type == GuestIdentity.ClaimType);
        }
        var articles = await client.GetAsync("/articles");
        Assert.Equal(HttpStatusCode.OK, articles.StatusCode);
        Assert.Contains("ゲストモード", WebUtility.HtmlDecode(await articles.Content.ReadAsStringAsync()));
        var admin = await client.GetAsync("/admin/users");
        Assert.Equal(HttpStatusCode.Redirect, admin.StatusCode);
        Assert.Contains("/forbidden", admin.Headers.Location?.OriginalString);
        var account = WebUtility.HtmlDecode(await client.GetStringAsync("/account"));
        Assert.DoesNotContain("action=\"/account/password\"", account);

        await SetCsrfTokenAsync(client);
        var repeated = await client.PostAsync("/login/guest", new FormUrlEncodedContent(
            new Dictionary<string, string> { ["returnUrl"] = "/articles" }));
        Assert.Equal(HttpStatusCode.Redirect, repeated.StatusCode);
        Assert.False(repeated.Headers.Contains("Set-Cookie"));

        await SetCsrfTokenAsync(client);
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync("/logout", null)).StatusCode);
        var afterLogout = await client.GetAsync("/articles");
        Assert.Equal(HttpStatusCode.Redirect, afterLogout.StatusCode);
        Assert.Contains("/login", afterLogout.Headers.Location?.OriginalString);
    }

    [Theory]
    [InlineData("https://example.com")]
    [InlineData("//example.com")]
    [InlineData("/\\example.com")]
    public async Task GuestLogin_WithUnsafeReturnUrl_StaysOnSite(string returnUrl)
    {
        using var factory = CreateFactory();
        using var client = CreateClient(factory);
        await SetCsrfTokenAsync(client);
        var response = await client.PostAsync("/login/guest", new FormUrlEncodedContent(
            new Dictionary<string, string> { ["returnUrl"] = returnUrl }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task NormalLogin_AfterGuestSession_RestoresNormalUserWithoutGuestClaim()
    {
        var userId = Guid.NewGuid().ToString("N");
        var email = $"{userId}@example.test";
        const string password = "Guest-transition-test-123!";
        await fixture.SeedUserWithPasswordAsync(userId, email, password, ApplicationRoles.User);
        using var factory = CreateFactory();
        using var client = CreateClient(factory);
        await SetCsrfTokenAsync(client);
        var guestResponse = await client.PostAsync("/login/guest", new FormUrlEncodedContent(
            new Dictionary<string, string> { ["returnUrl"] = "/articles" }));
        Assert.True(GuestIdentity.IsGuest(ReadTicket(factory, guestResponse).Principal));

        await SetCsrfTokenAsync(client);
        var response = await client.PostAsync("/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["email"] = email,
            ["password"] = password,
            ["returnUrl"] = "/articles"
        }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var ticket = ReadTicket(factory, response);
        Assert.Equal(userId, ticket.Principal.FindFirstValue(ClaimTypes.NameIdentifier));
        Assert.False(GuestIdentity.IsGuest(ticket.Principal));
        Assert.DoesNotContain("ゲストモード", WebUtility.HtmlDecode(await client.GetStringAsync("/articles")));
    }

    [Fact]
    public async Task GuestLogin_SeparateBrowsers_CannotReadEachOthersArticles()
    {
        using var factory = CreateFactory();
        using var first = CreateClient(factory);
        using var second = CreateClient(factory);
        var ids = new List<string>();
        foreach (var client in new[] { first, second })
        {
            await SetCsrfTokenAsync(client);
            var response = await client.PostAsync("/login/guest", new FormUrlEncodedContent(
                new Dictionary<string, string> { ["returnUrl"] = "/articles" }));
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            ids.Add(ReadTicket(factory, response).Principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
        }
        Assert.NotEqual(ids[0], ids[1]);
        var articleId = await fixture.SeedArticleAsync(ids[0], "ゲスト専用記事");
        Assert.Equal(HttpStatusCode.OK, (await first.GetAsync($"/api/articles/{articleId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await second.GetAsync($"/api/articles/{articleId}")).StatusCode);
        await SetCsrfTokenAsync(first);
        var search = await first.PostAsJsonAsync($"/api/articles/{articleId}/research/web", new { query = "家庭菜園", maxResults = 3 });
        Assert.Equal(HttpStatusCode.Accepted, search.StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var job = await db.ArticleGenerationJobs.SingleAsync(item => item.ArticleId == articleId);
        using var payload = System.Text.Json.JsonDocument.Parse(job.PayloadJson!);
        Assert.True(payload.RootElement.GetProperty("isDummy").GetBoolean());
    }

    [Fact]
    public async Task GuestLogin_WithoutCsrfToken_IsRejected()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await client.PostAsync("/login/guest", new FormUrlEncodedContent(
            new Dictionary<string, string> { ["returnUrl"] = "/articles" }));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private WebApplicationFactory<Program> CreateFactory() => fixture.Factory.WithWebHostBuilder(builder =>
    {
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?> { ["ExternalApis:UseMocks"] = "false" }));
        builder.ConfigureTestServices(services => services.PostConfigure<AuthenticationOptions>(options =>
        {
            options.DefaultAuthenticateScheme = IdentityConstants.ApplicationScheme;
            options.DefaultChallengeScheme = IdentityConstants.ApplicationScheme;
        }));
    });

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory) => factory.CreateClient(
        new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });

    private static AuthenticationTicket ReadTicket(WebApplicationFactory<Program> factory, HttpResponseMessage response)
    {
        var options = factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(IdentityConstants.ApplicationScheme);
        var cookie = response.Headers.GetValues("Set-Cookie")
            .Single(value => value.StartsWith(options.Cookie.Name + "=", StringComparison.Ordinal));
        var value = cookie.Split(';')[0][(options.Cookie.Name!.Length + 1)..];
        return options.TicketDataFormat.Unprotect(value)!;
    }

    private static async Task SetCsrfTokenAsync(HttpClient client)
    {
        var html = await client.GetStringAsync("/login");
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(match.Success);
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", WebUtility.HtmlDecode(match.Groups[1].Value));
    }
}
