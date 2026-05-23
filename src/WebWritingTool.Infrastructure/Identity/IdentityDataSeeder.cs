using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WebWritingTool.Application.Security;
using WebWritingTool.Domain.Ai;
using WebWritingTool.Infrastructure.Data;

namespace WebWritingTool.Infrastructure.Identity;

public sealed class IdentityDataSeeder(
    RoleManager<IdentityRole> roleManager,
    UserManager<ApplicationUser> userManager,
    ApplicationDbContext dbContext,
    IOptions<AdminSeedOptions> options,
    ILogger<IdentityDataSeeder> logger)
    : IIdentityDataSeeder
{
    private const string InitialAiProvider = "GoogleGemini";
    private const string InitialAiModel = "gemini-3.5-flash";
    private const string InitialAiDisplayName = "Google Gemini 3.5 Flash";
    private const string InitialAiRegion = "Japan";

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        await SeedRolesAsync(cancellationToken);
        await SeedAiModelSettingsAsync(cancellationToken);

        var admins = await userManager.GetUsersInRoleAsync(ApplicationRoles.Admin);
        if (admins.Count > 0)
        {
            logger.LogInformation("Admin seed skipped because at least one Admin user already exists.");
            return;
        }

        var seed = options.Value;
        if (string.IsNullOrWhiteSpace(seed.Email) || string.IsNullOrWhiteSpace(seed.Password))
        {
            logger.LogInformation("Initial Admin user seed skipped because AdminSeed credentials are not configured.");
            return;
        }

        var admin = new ApplicationUser
        {
            UserName = seed.Email,
            Email = seed.Email,
            EmailConfirmed = true,
            DisplayName = string.IsNullOrWhiteSpace(seed.DisplayName) ? "Admin" : seed.DisplayName,
            IsEnabled = true
        };

        var createResult = await userManager.CreateAsync(admin, seed.Password);
        EnsureSucceeded(createResult, "Failed to seed initial Admin user.");

        var roleAssignResult = await userManager.AddToRoleAsync(admin, ApplicationRoles.Admin);
        EnsureSucceeded(roleAssignResult, "Failed to assign Admin role to the initial Admin user.");

        logger.LogInformation("Initial Admin user was seeded.");
    }

    private async Task SeedRolesAsync(CancellationToken cancellationToken)
    {
        foreach (var role in ApplicationRoles.All)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!await roleManager.RoleExistsAsync(role))
            {
                var roleResult = await roleManager.CreateAsync(new IdentityRole(role));
                EnsureSucceeded(roleResult, $"Failed to seed role '{role}'.");
            }
        }
    }

    private async Task SeedAiModelSettingsAsync(CancellationToken cancellationToken)
    {
        var exists = await dbContext.AiModelSettings.AnyAsync(
            setting => setting.Provider == InitialAiProvider && setting.Model == InitialAiModel,
            cancellationToken);

        if (exists)
        {
            logger.LogInformation("Initial AI model seed skipped because the model already exists.");
            return;
        }

        dbContext.AiModelSettings.Add(new AiModelSetting
        {
            Provider = InitialAiProvider,
            Model = InitialAiModel,
            DisplayName = InitialAiDisplayName,
            Region = InitialAiRegion,
            Enabled = true,
            SortOrder = 0
        });

        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Initial AI model was seeded.");
    }

    private static void EnsureSucceeded(IdentityResult result, string message)
    {
        if (result.Succeeded)
        {
            return;
        }

        var errors = string.Join(", ", result.Errors.Select(error => error.Code));
        throw new InvalidOperationException($"{message} Identity errors: {errors}");
    }
}
