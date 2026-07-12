using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using WebWritingTool.Application.Security;
using WebWritingTool.IntegrationTests.Support;

namespace WebWritingTool.IntegrationTests.Api;

[Collection(IntegrationTestCollection.Name)]
public class AccountApiTests(IntegrationTestFixture fixture)
{
    private const string CurrentPassword = "Current-password-123!";
    private const string NewPassword = "New-password-456!";

    [Fact]
    public async Task ChangePassword_WithValidCredentials_ChangesPassword()
    {
        var userId = $"password-change-{Guid.NewGuid():N}";
        await fixture.SeedUserWithPasswordAsync(
            userId,
            $"{userId}@example.test",
            CurrentPassword,
            ApplicationRoles.User);

        using var client = await fixture.CreateAuthenticatedClientAsync(userId, ApplicationRoles.User);
        var response = await client.PutAsJsonAsync("/api/account/password", new
        {
            currentPassword = CurrentPassword,
            newPassword = NewPassword,
            confirmNewPassword = NewPassword
        });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.False(await fixture.CheckPasswordAsync(userId, CurrentPassword));
        Assert.True(await fixture.CheckPasswordAsync(userId, NewPassword));
    }

    [Fact]
    public async Task ChangePassword_WithInvalidCurrentPassword_ReturnsBadRequestAndKeepsPassword()
    {
        var userId = $"password-current-{Guid.NewGuid():N}";
        await fixture.SeedUserWithPasswordAsync(
            userId,
            $"{userId}@example.test",
            CurrentPassword,
            ApplicationRoles.User);

        using var client = await fixture.CreateAuthenticatedClientAsync(userId, ApplicationRoles.User);
        var response = await client.PutAsJsonAsync("/api/account/password", new
        {
            currentPassword = "Wrong-password-123!",
            newPassword = NewPassword,
            confirmNewPassword = NewPassword
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(await fixture.CheckPasswordAsync(userId, CurrentPassword));
        Assert.False(await fixture.CheckPasswordAsync(userId, NewPassword));
    }

    [Fact]
    public async Task ChangePassword_WithMismatchedConfirmation_ReturnsBadRequestAndKeepsPassword()
    {
        var userId = $"password-confirm-{Guid.NewGuid():N}";
        await fixture.SeedUserWithPasswordAsync(
            userId,
            $"{userId}@example.test",
            CurrentPassword,
            ApplicationRoles.User);

        using var client = await fixture.CreateAuthenticatedClientAsync(userId, ApplicationRoles.User);
        var response = await client.PutAsJsonAsync("/api/account/password", new
        {
            currentPassword = CurrentPassword,
            newPassword = NewPassword,
            confirmNewPassword = "Different-password-789!"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(await fixture.CheckPasswordAsync(userId, CurrentPassword));
    }

    [Fact]
    public async Task ChangePassword_WithWeakNewPassword_ReturnsBadRequestAndKeepsPassword()
    {
        var userId = $"password-weak-{Guid.NewGuid():N}";
        await fixture.SeedUserWithPasswordAsync(
            userId,
            $"{userId}@example.test",
            CurrentPassword,
            ApplicationRoles.User);

        using var client = await fixture.CreateAuthenticatedClientAsync(userId, ApplicationRoles.User);
        var response = await client.PutAsJsonAsync("/api/account/password", new
        {
            currentPassword = CurrentPassword,
            newPassword = "weak",
            confirmNewPassword = "weak"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(await fixture.CheckPasswordAsync(userId, CurrentPassword));
    }

    [Fact]
    public async Task ChangePassword_WithSamePasswordAsCurrent_ReturnsBadRequestAndKeepsPassword()
    {
        var userId = $"password-same-{Guid.NewGuid():N}";
        await fixture.SeedUserWithPasswordAsync(
            userId,
            $"{userId}@example.test",
            CurrentPassword,
            ApplicationRoles.User);

        using var client = await fixture.CreateAuthenticatedClientAsync(userId, ApplicationRoles.User);
        var response = await client.PutAsJsonAsync("/api/account/password", new
        {
            currentPassword = CurrentPassword,
            newPassword = CurrentPassword,
            confirmNewPassword = CurrentPassword
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(await fixture.CheckPasswordAsync(userId, CurrentPassword));
    }

    [Fact]
    public async Task ChangePassword_OverRateLimit_ReturnsTooManyRequests()
    {
        var userId = $"password-ratelimit-{Guid.NewGuid():N}";
        using var client = await fixture.CreateAuthenticatedClientAsync(userId, ApplicationRoles.User);

        // Mismatched confirmation short-circuits before any password hashing,
        // so exhausting the Test-environment window (100 permits) stays fast.
        var request = new
        {
            currentPassword = CurrentPassword,
            newPassword = NewPassword,
            confirmNewPassword = "Mismatched-password-789!"
        };

        for (var i = 0; i < 100; i++)
        {
            var response = await client.PutAsJsonAsync("/api/account/password", request);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        var limited = await client.PutAsJsonAsync("/api/account/password", request);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal("application/problem+json", limited.Content.Headers.ContentType?.MediaType);

        var problem = await limited.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(StatusCodes.Status429TooManyRequests, problem.Status);
        Assert.Equal("RateLimited", problem.Title);
    }
}
