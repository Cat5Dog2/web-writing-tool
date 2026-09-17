using Microsoft.Extensions.DependencyInjection;
using WebWritingTool.Application.Security;
using WebWritingTool.Application.Wordpress;
using WebWritingTool.Domain.Wordpress;
using WebWritingTool.Infrastructure.Data;
using WebWritingTool.IntegrationTests.Support;

namespace WebWritingTool.IntegrationTests.Api;

[Collection(IntegrationTestCollection.Name)]
public class WordpressSiteServiceIntegrationTests(IntegrationTestFixture fixture)
{
    [Fact]
    public async Task GetCategoriesAsync_WhenStoredPasswordCannotBeDecrypted_ReturnsUnauthorizedInsteadOfThrowing()
    {
        var (actor, site) = await SeedUserAndSiteWithUndecryptablePasswordAsync("categories");

        using var scope = fixture.Factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IWordpressSiteQueryService>();

        var result = await service.GetCategoriesAsync(actor, site.Id);

        Assert.False(result.Succeeded);
        // Unauthorized (not the generic ExternalFailure) signals that posting would fail with this
        // same credential too, not just the category fetch -- Articles.razor uses this distinction
        // to avoid telling the user they can still post (mobile-ui-dialog-decrypt-followup-2026-09-17 review).
        Assert.Equal(WordpressServiceError.Unauthorized, result.Error);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task TestConnectionAsync_WhenStoredPasswordCannotBeDecrypted_ReturnsExternalFailureInsteadOfThrowing()
    {
        var (actor, site) = await SeedUserAndSiteWithUndecryptablePasswordAsync("test-connection");

        using var scope = fixture.Factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IWordpressSiteCommandService>();

        var result = await service.TestConnectionAsync(actor, site.Id);

        Assert.False(result.Succeeded);
        Assert.Equal(WordpressServiceError.ExternalFailure, result.Error);
        Assert.Null(result.Value);
    }

    private async Task<(WordpressActor Actor, WordpressSite Site)> SeedUserAndSiteWithUndecryptablePasswordAsync(
        string suffix)
    {
        var userId = $"wordpress-decrypt-{suffix}-{Guid.NewGuid():N}";
        await fixture.SeedUserAsync(userId, $"{userId}@example.test", ApplicationRoles.User);

        using var scope = fixture.Factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // A site saved through Settings.razor always has a real Data-Protection-encrypted value
        // here. This plain placeholder simulates the only realistic way this field stops being
        // decryptable in production: the Data Protection key ring that wrote it is gone or
        // rotated, so IDataProtector.Unprotect can no longer read what is already stored.
        var site = new WordpressSite
        {
            UserId = userId,
            SiteName = $"site-{suffix}",
            BaseUrl = "https://example.com",
            LoginId = "wp-user",
            EncryptedApplicationPassword = "not-a-valid-protected-value"
        };
        dbContext.WordpressSites.Add(site);
        await dbContext.SaveChangesAsync();

        return (new WordpressActor(userId, IsAdmin: false), site);
    }
}
