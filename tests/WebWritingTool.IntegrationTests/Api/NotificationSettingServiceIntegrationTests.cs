using Microsoft.Extensions.DependencyInjection;
using WebWritingTool.Application.Notifications;
using WebWritingTool.Application.Security;
using WebWritingTool.Domain.Notifications;
using WebWritingTool.Infrastructure.Data;
using WebWritingTool.IntegrationTests.Support;

namespace WebWritingTool.IntegrationTests.Api;

[Collection(IntegrationTestCollection.Name)]
public class NotificationSettingServiceIntegrationTests(IntegrationTestFixture fixture)
{
    [Fact]
    public async Task SendTestAsync_WhenStoredWebhookUrlCannotBeDecrypted_ReportsFailureInsteadOfThrowing()
    {
        var userId = $"notification-decrypt-{Guid.NewGuid():N}";
        await fixture.SeedUserAsync(userId, $"{userId}@example.test", ApplicationRoles.User);

        using var scope = fixture.Factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // A setting saved through Settings.razor always has a real Data-Protection-encrypted
        // value here. This plain placeholder simulates the only realistic way this field stops
        // being decryptable in production: the Data Protection key ring that wrote it is gone or
        // rotated, so IDataProtector.Unprotect can no longer read what is already stored.
        dbContext.NotificationSettings.Add(new NotificationSetting
        {
            UserId = userId,
            Provider = NotificationProviders.Discord,
            DestinationMasked = "https://discord.com/api/webhooks/***",
            EncryptedWebhookUrl = "not-a-valid-protected-value",
            Enabled = true
        });
        await dbContext.SaveChangesAsync();

        var service = scope.ServiceProvider.GetRequiredService<INotificationTestService>();

        var result = await service.SendTestAsync(
            new SendTestNotificationCommand(
                new NotificationActor(userId, IsAdmin: false),
                NotificationProviders.Discord,
                Destination: null));

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Value);
        Assert.False(result.Value.Success);
        Assert.Contains("復号", result.Value.Message, StringComparison.Ordinal);
    }
}
