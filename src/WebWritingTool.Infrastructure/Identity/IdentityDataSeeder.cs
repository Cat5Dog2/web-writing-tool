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
    private const string InitialAiRegion = "Japan";

    // SortOrderの昇順が画面の選択肢順であり、先頭が既定モデルになる。
    // このシードはここに列挙したモデルだけを整列させる。手動追加した別モデルがSortOrder 0を持つ場合、
    // 既定モデルが先頭になる保証はない。運用側の並び順を勝手に書き換えないための割り切りであり、
    // 手動追加モデルまで保証するなら明示的な既定モデル列をAiModelSettingsへ追加する。
    private static readonly (string Model, string DisplayName, int SortOrder)[] InitialAiModels =
    [
        ("gemini-3.6-flash", "Google Gemini 3.6 Flash", 0),
        ("gemini-3.5-flash", "Google Gemini 3.5 Flash", 1)
    ];

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
        var existingSettings = await dbContext.AiModelSettings
            .Where(setting => setting.Provider == InitialAiProvider)
            .ToDictionaryAsync(setting => setting.Model, cancellationToken);

        var added = 0;
        var reordered = 0;

        foreach (var (model, displayName, sortOrder) in InitialAiModels)
        {
            if (existingSettings.TryGetValue(model, out var existing))
            {
                // 既存行のEnabledは運用側の設定として尊重し、既定モデルの並び順だけを合わせる。
                if (existing.SortOrder != sortOrder)
                {
                    existing.SortOrder = sortOrder;
                    reordered++;
                }

                continue;
            }

            dbContext.AiModelSettings.Add(new AiModelSetting
            {
                Provider = InitialAiProvider,
                Model = model,
                DisplayName = displayName,
                Region = InitialAiRegion,
                Enabled = true,
                SortOrder = sortOrder
            });
            added++;
        }

        if (added == 0 && reordered == 0)
        {
            logger.LogInformation("Initial AI model seed skipped because all models are already registered.");
            return;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Initial AI models were seeded. added={AddedCount} reordered={ReorderedCount}",
            added,
            reordered);
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
