using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WebWritingTool.Application.Articles;
using WebWritingTool.Application.Generation;
using WebWritingTool.Application.Search;
using WebWritingTool.Domain.Articles;
using WebWritingTool.Domain.Jobs;
using WebWritingTool.Infrastructure.Data;

namespace WebWritingTool.Infrastructure.BackgroundJobs;

public sealed record BulkGenerationSettings(BulkGenerationOptions Options, int? H2Count, int? H3Count);

public sealed class BulkGenerationWorkflow(ApplicationDbContext db, JobRetryPolicy retryPolicy)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static BulkGenerationSettings Settings(ArticleGenerationRun run) =>
        JsonSerializer.Deserialize<BulkGenerationSettings>(run.SettingsJson, JsonOptions)
        ?? throw new InvalidOperationException("自動生成設定を読み込めません。");

    public ArticleGenerationJob Start(Article article, BulkCreateArticlesCommand command, Guid batchId)
    {
        var run = new ArticleGenerationRun
        {
            ArticleId = article.Id,
            UserId = article.UserId,
            BatchId = batchId,
            SettingsJson = JsonSerializer.Serialize(new BulkGenerationSettings(command.Generation!, command.H2Count, command.H3Count), JsonOptions)
        };
        db.ArticleGenerationRuns.Add(run);
        return AddJob(run, article, Stages(run, article).First());
    }

    // The caller commits the completed job and its successor in the same transaction.
    public async Task AdvanceAsync(ArticleGenerationJob job, CancellationToken cancellationToken)
    {
        if (job.GenerationRunId is not Guid runId) return;
        var run = await db.ArticleGenerationRuns.SingleAsync(r => r.Id == runId, cancellationToken);
        await db.Entry(run).ReloadAsync(cancellationToken);
        var article = await db.Articles.SingleAsync(a => a.Id == run.ArticleId, cancellationToken);
        await db.Entry(article).ReloadAsync(cancellationToken);
        if (job.JobType is JobType.WebSearch or JobType.XFullArchiveSearch && job.ResultJson is not null)
        {
            using var result = JsonDocument.Parse(job.ResultJson);
            var field = job.JobType == JobType.WebSearch ? "resultCount" : "postCount";
            if (result.RootElement.TryGetProperty(field, out var count) && count.GetInt32() == 0)
                run.Warning = string.Join(" ", new[] { run.Warning, $"{(job.JobType == JobType.WebSearch ? "Web" : "X")}検索結果は0件でした。" }.Where(s => s is not null));
        }

        var stages = Stages(run, article);
        var completed = await db.ArticleGenerationJobs.Where(j => j.GenerationRunId == run.Id && j.Status == JobStatus.Succeeded)
            .Select(j => j.JobType).ToListAsync(cancellationToken);
        completed.Add(job.JobType);
        var next = stages.Where(stage => !completed.Contains(stage)).Cast<JobType?>().FirstOrDefault();
        if (run.StopRequested || next is null)
        {
            run.Status = run.StopRequested && next is not null ? JobStatus.Canceled : JobStatus.Succeeded;
            if (run.Status == JobStatus.Canceled) await SetStoppedArticleStateAsync(article.Id, cancellationToken);
            run.FinishedAt = DateTimeOffset.UtcNow;
            return;
        }

        AddJob(run, article, next.Value);
    }

    public async Task SetStoppedArticleStateAsync(Guid articleId, CancellationToken cancellationToken)
    {
        var article = await db.Articles.SingleAsync(a => a.Id == articleId, cancellationToken);
        if (article.Status != ArticleStatus.Completed)
            article.Status = await db.ArticleHeadings.AnyAsync(h => h.ArticleId == articleId, cancellationToken)
                ? ArticleStatus.OutlineReady : ArticleStatus.Draft;
    }

    private static IReadOnlyList<JobType> Stages(ArticleGenerationRun run, Article article)
    {
        var options = Settings(run).Options;
        var stages = new List<JobType>();
        if (options.UseWebSearch) stages.Add(JobType.WebSearch);
        if (options.UseXSearch) stages.Add(JobType.XFullArchiveSearch);
        if (string.IsNullOrWhiteSpace(article.Title)) stages.Add(JobType.TitleGeneration);
        stages.Add(JobType.OutlineGeneration);
        if (options.Scope == "FullArticle") stages.Add(JobType.BodyGeneration);
        return stages;
    }

    private ArticleGenerationJob AddJob(ArticleGenerationRun run, Article article, JobType type)
    {
        var settings = Settings(run);
        var options = settings.Options;
        object payload = type switch
        {
            JobType.WebSearch => new WebSearchJobPayload(article.Id, null, article.Keyword, "Japan", "ja",
                options.WebResultCount, article.IsDomesticOnly, null, "basic", null, null, null, IsManual: false),
            JobType.XFullArchiveSearch => new XFullArchiveSearchJobPayload(article.Id, null, article.Keyword, "ja",
                run.CreatedAt.AddDays(-options.XSearchDays), run.CreatedAt, options.XResultCount, false, true, true, null),
            JobType.TitleGeneration => new TitleGenerationPayload(article.Id, article.Keyword, article.GenerationModel, 1, "Ai", null, null, null),
            JobType.OutlineGeneration => new OutlineGenerationPayload(article.Id, article.Keyword, article.Title,
                settings.H2Count, settings.H3Count, "Ai", article.GenerationModel, false, article.IsDomesticOnly,
                article.Tone, article.SuggestedKeywords, article.RelatedKeywords, article.LearningType, article.LearningText, article.AdditionalPrompt),
            JobType.BodyGeneration => new BodyGenerationPayload(article.Id, null, "MissingOnly", article.GenerationModel, null, false, article.AdditionalPrompt),
            _ => throw new InvalidOperationException("未対応の自動生成段階です。")
        };
        var job = new ArticleGenerationJob
        {
            GenerationRunId = run.Id,
            ArticleId = article.Id,
            UserId = article.UserId,
            JobType = type,
            PayloadJson = JsonSerializer.Serialize(payload, JsonOptions),
            QueuedAt = DateTimeOffset.UtcNow,
            MaxAttempts = retryPolicy.GetMaxAttempts(type)
        };
        run.Stage = type;
        run.Status = JobStatus.Queued;
        article.Status = type == JobType.BodyGeneration ? ArticleStatus.BodyQueued : ArticleStatus.OutlineQueued;
        db.ArticleGenerationJobs.Add(job);
        return job;
    }
}
