using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WebWritingTool.Application.Articles;
using WebWritingTool.Application.Security;
using WebWritingTool.Domain.Ai;
using WebWritingTool.Infrastructure.Data;
using WebWritingTool.Infrastructure.Identity;
using WebWritingTool.IntegrationTests.Support;

namespace WebWritingTool.IntegrationTests.Data;

[Collection(IntegrationTestCollection.Name)]
public class AiModelSeedIntegrationTests(IntegrationTestFixture fixture)
{
    private const string Provider = "GoogleGemini";
    private const string DefaultModel = "gemini-3.6-flash";
    private const string LegacyModel = "gemini-3.5-flash";

    [Fact]
    public async Task SeedAsync_WhenLegacyModelIsFirst_PromotesDefaultModelToTop()
    {
        try
        {
            await ReplaceAiModelSettingsAsync(new AiModelSetting
            {
                Provider = Provider,
                Model = LegacyModel,
                DisplayName = "Google Gemini 3.5 Flash",
                Region = "Japan",
                Enabled = true,
                SortOrder = 0
            });

            await RunSeederAsync();

            var settings = await GetGeminiModelsAsync();
            Assert.Equal(0, settings[DefaultModel].SortOrder);
            Assert.Equal(1, settings[LegacyModel].SortOrder);
        }
        finally
        {
            await RestoreCanonicalSeedAsync();
        }
    }

    [Fact]
    public async Task SeedAsync_WhenLegacyModelIsDisabled_KeepsEnabledFlag()
    {
        try
        {
            await ReplaceAiModelSettingsAsync(new AiModelSetting
            {
                Provider = Provider,
                Model = LegacyModel,
                DisplayName = "Google Gemini 3.5 Flash",
                Region = "Japan",
                Enabled = false,
                SortOrder = 0
            });

            await RunSeederAsync();

            var settings = await GetGeminiModelsAsync();
            Assert.False(settings[LegacyModel].Enabled);
            Assert.True(settings[DefaultModel].Enabled);
        }
        finally
        {
            await RestoreCanonicalSeedAsync();
        }
    }

    [Fact]
    public async Task SeedAsync_RunTwice_DoesNotDuplicateModels()
    {
        try
        {
            await ReplaceAiModelSettingsAsync();

            await RunSeederAsync();
            await RunSeederAsync();

            using var scope = fixture.Factory.Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var models = await dbContext.AiModelSettings
                .AsNoTracking()
                .Where(setting => setting.Provider == Provider)
                .Select(setting => setting.Model)
                .ToListAsync();

            Assert.Equal(2, models.Count);
            Assert.Equal(models.Count, models.Distinct().Count());
        }
        finally
        {
            await RestoreCanonicalSeedAsync();
        }
    }

    [Fact]
    public async Task GetFormOptionsAsync_ReturnsDefaultModelFirstAndKeepsLegacyModel()
    {
        await RestoreCanonicalSeedAsync();
        var userId = $"model-options-{Guid.NewGuid():N}";
        await fixture.SeedUserAsync(userId, $"{userId}@example.test", ApplicationRoles.User);

        using var scope = fixture.Factory.Services.CreateScope();
        var queryService = scope.ServiceProvider.GetRequiredService<IArticleQueryService>();

        var options = await queryService.GetFormOptionsAsync(userId);

        var models = options.GenerationModels.Select(model => model.Model).ToArray();
        Assert.Equal(DefaultModel, models[0]);
        Assert.Contains(LegacyModel, models);
    }

    [Fact]
    public async Task CreateArticle_WithLegacyModel_IsAccepted()
    {
        await RestoreCanonicalSeedAsync();
        var suffix = Guid.NewGuid().ToString("N");
        var userId = $"legacy-model-{suffix}";
        await fixture.SeedUserAsync(userId, $"{userId}@example.test", ApplicationRoles.User);

        using var client = await fixture.CreateAuthenticatedClientAsync(userId, ApplicationRoles.User);
        var response = await client.PostAsJsonAsync("/api/articles", new
        {
            keyword = $"legacy-keyword-{suffix}",
            title = "Legacy model article",
            generateImage = false,
            h2Count = 2,
            h3Count = 1,
            tone = "calm",
            tags = new[] { "test" },
            memo = (string?)null,
            suggestedKeywords = (string?)null,
            relatedKeywords = (string?)null,
            learningType = "None",
            learningText = (string?)null,
            additionalPrompt = (string?)null,
            writingProfileWordpressSiteId = (Guid?)null,
            outlineMethod = "Keyword",
            generationModel = LegacyModel,
            searchMode = false,
            isDomesticOnly = true,
            notificationMode = "None"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<CreateArticleResponse>();
        Assert.NotNull(created);

        using var scope = fixture.Factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var stored = await dbContext.Articles
            .AsNoTracking()
            .SingleAsync(article => article.Id == created.Id);

        Assert.Equal(LegacyModel, stored.GenerationModel);
    }

    private async Task RunSeederAsync()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var seeder = scope.ServiceProvider.GetRequiredService<IIdentityDataSeeder>();
        await seeder.SeedAsync();
    }

    private async Task ReplaceAiModelSettingsAsync(params AiModelSetting[] settings)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await dbContext.AiModelSettings.ExecuteDeleteAsync();

        if (settings.Length > 0)
        {
            dbContext.AiModelSettings.AddRange(settings);
            await dbContext.SaveChangesAsync();
        }
    }

    private async Task RestoreCanonicalSeedAsync()
    {
        await ReplaceAiModelSettingsAsync();
        await RunSeederAsync();
    }

    private async Task<Dictionary<string, AiModelSetting>> GetGeminiModelsAsync()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await dbContext.AiModelSettings
            .AsNoTracking()
            .Where(setting => setting.Provider == Provider)
            .ToDictionaryAsync(setting => setting.Model);
    }
}
