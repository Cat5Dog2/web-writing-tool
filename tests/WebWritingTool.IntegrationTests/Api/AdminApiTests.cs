using System.Net;
using System.Net.Http.Json;
using WebWritingTool.Application.Admin;
using WebWritingTool.Application.Security;
using WebWritingTool.IntegrationTests.Support;

namespace WebWritingTool.IntegrationTests.Api;

[Collection(IntegrationTestCollection.Name)]
public class AdminApiTests(IntegrationTestFixture fixture)
{
    [Fact]
    public async Task GetUsers_AsRegularUser_ReturnsForbidden()
    {
        var userId = $"regular-{Guid.NewGuid():N}";
        using var client = await fixture.CreateAuthenticatedClientAsync(userId, ApplicationRoles.User);

        var response = await client.GetAsync("/api/admin/users");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CreateUser_AsAdmin_CreatesUserWithoutReturningPassword()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var adminUserId = $"admin-{suffix}";
        await fixture.SeedUserAsync(adminUserId, $"{adminUserId}@example.test", ApplicationRoles.Admin);

        using var client = await fixture.CreateAuthenticatedClientAsync(adminUserId, ApplicationRoles.Admin);
        var password = "Change-this-test-password-123!";
        var response = await client.PostAsJsonAsync("/api/admin/users", new
        {
            email = $"created-{suffix}@example.test",
            displayName = "Created User",
            password,
            role = ApplicationRoles.User,
            isEnabled = true,
            monthlyLimitChars = 12345,
            remainingOutlineCount = 7
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(password, body, StringComparison.Ordinal);

        var created = await response.Content.ReadFromJsonAsync<AdminUserResponse>();
        Assert.NotNull(created);
        Assert.Equal("Created User", created.DisplayName);
        Assert.Equal(ApplicationRoles.User, created.Role);
        Assert.Equal(12345, created.MonthlyLimitChars);
        Assert.Equal(7, created.RemainingOutlineCount);

        var list = await client.GetFromJsonAsync<AdminUserListResponse>(
            $"/api/admin/users?q={Uri.EscapeDataString(created.Email!)}");
        Assert.NotNull(list);
        Assert.Contains(list.Items, item => item.Id == created.Id);
    }
}
