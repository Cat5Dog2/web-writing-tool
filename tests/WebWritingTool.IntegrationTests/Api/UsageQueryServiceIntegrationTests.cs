using Microsoft.Extensions.DependencyInjection;
using WebWritingTool.Application.Security;
using WebWritingTool.Application.Usage;
using WebWritingTool.Domain.Usage;
using WebWritingTool.Infrastructure.Data;
using WebWritingTool.IntegrationTests.Support;

namespace WebWritingTool.IntegrationTests.Api;

[Collection(IntegrationTestCollection.Name)]
public class UsageQueryServiceIntegrationTests(IntegrationTestFixture fixture)
{
    [Fact]
    public async Task ListLedgersAsync_WithDateAndPaging_ReturnsOnlyOwnedLedgersInRange()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var userId = $"usage-user-{suffix}";
        var otherUserId = $"usage-other-{suffix}";
        await fixture.SeedUserAsync(userId, $"{userId}@example.test", ApplicationRoles.User);
        await fixture.SeedUserAsync(otherUserId, $"{otherUserId}@example.test", ApplicationRoles.User);

        using var scope = fixture.Factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.UsageLedgers.AddRange(
            CreateLedger(userId, "in-range-later", new DateTimeOffset(2026, 7, 4, 12, 0, 0, TimeSpan.Zero)),
            CreateLedger(userId, "in-range-earlier", new DateTimeOffset(2026, 7, 3, 12, 0, 0, TimeSpan.Zero)),
            CreateLedger(userId, "out-of-range", new DateTimeOffset(2026, 7, 2, 23, 59, 59, TimeSpan.Zero)),
            CreateLedger(otherUserId, "other-user", new DateTimeOffset(2026, 7, 4, 13, 0, 0, TimeSpan.Zero)));
        await dbContext.SaveChangesAsync();
        var service = scope.ServiceProvider.GetRequiredService<IUsageQueryService>();

        var response = await service.ListLedgersAsync(
            new UsageActor(userId, IsAdmin: false),
            new UsageLedgerQuery(
                Page: 1,
                PageSize: 1,
                From: new DateOnly(2026, 7, 3),
                To: new DateOnly(2026, 7, 4)));

        Assert.Equal(2, response.TotalCount);
        Assert.Equal(2, response.TotalPages);
        Assert.False(response.HasPrevious);
        Assert.True(response.HasNext);
        Assert.Equal("in-range-later", Assert.Single(response.Items).Operation);
    }

    private static UsageLedger CreateLedger(string userId, string operation, DateTimeOffset occurredAt)
    {
        return new UsageLedger
        {
            UserId = userId,
            Provider = "test",
            Model = "test-model",
            Operation = operation,
            PromptChars = 10,
            OutputChars = 20,
            UsageChars = 30,
            OccurredAt = occurredAt
        };
    }
}
