using Microsoft.EntityFrameworkCore;
using WebWritingTool.Application.Articles;
using WebWritingTool.Domain.Articles;
using WebWritingTool.Domain.Jobs;
using WebWritingTool.Infrastructure.BackgroundJobs;
using WebWritingTool.Infrastructure.Data;

namespace WebWritingTool.Infrastructure.Articles;

public sealed class BulkGenerationService(ApplicationDbContext db, BulkGenerationWorkflow workflow) : IBulkGenerationService
{
    public async Task<IReadOnlyList<BulkGenerationBatch>> GetBatchesAsync(
        ArticleActor actor, CancellationToken cancellationToken = default) =>
        await db.ArticleGenerationRuns.AsNoTracking()
            .Where(r => (r.UserId == actor.UserId || actor.IsAdmin) && db.Articles.Any(a => a.Id == r.ArticleId))
            .GroupBy(r => r.BatchId)
            .OrderByDescending(g => g.Min(r => r.CreatedAt))
            .Select(g => new BulkGenerationBatch(g.Key, g.Min(r => r.CreatedAt), g.Count()))
            .ToListAsync(cancellationToken);

    public Task<IReadOnlyList<BulkGenerationProgress>> GetOverviewAsync(
        ArticleActor actor, Guid? batchId = null, CancellationToken cancellationToken = default) =>
        ReadProgressAsync(actor, batchId, null, true, cancellationToken);

    public Task<IReadOnlyList<BulkGenerationProgress>> GetProgressAsync(
        ArticleActor actor, Guid? batchId = null, Guid? articleId = null, CancellationToken cancellationToken = default)
        => ReadProgressAsync(actor, batchId, articleId, false, cancellationToken);

    private async Task<IReadOnlyList<BulkGenerationProgress>> ReadProgressAsync(
        ArticleActor actor, Guid? batchId, Guid? articleId, bool includeUnfinished, CancellationToken cancellationToken)
    {
        var query = db.ArticleGenerationRuns.AsNoTracking()
            .Where(r => (r.UserId == actor.UserId || actor.IsAdmin) && db.Articles.Any(a => a.Id == r.ArticleId));
        if (articleId.HasValue) query = query.Where(r => r.ArticleId == articleId);
        else
        {
            batchId ??= await query.OrderByDescending(r => r.CreatedAt).Select(r => (Guid?)r.BatchId).FirstOrDefaultAsync(cancellationToken);
            query = query.Where(r => r.BatchId == batchId || includeUnfinished && r.Status != JobStatus.Succeeded);
        }
        var rows = await query.OrderBy(r => r.CreatedAt).Select(r => new
        {
            Run = r,
            Keyword = db.Articles.Where(a => a.Id == r.ArticleId).Select(a => a.Keyword).First(),
            Title = db.Articles.Where(a => a.Id == r.ArticleId).Select(a => a.Title).First(),
            Total = db.ArticleHeadings.Count(h => h.ArticleId == r.ArticleId),
            Done = db.ArticleHeadings.Count(h => h.ArticleId == r.ArticleId && h.Status == HeadingStatus.Generated),
            Error = db.ArticleGenerationJobs.Where(j => j.GenerationRunId == r.Id && j.JobType == r.Stage)
                .Select(j => j.ErrorMessage).FirstOrDefault()
        }).ToListAsync(cancellationToken);
        return rows.Select(row => new BulkGenerationProgress(row.Run.Id, row.Run.BatchId, row.Run.ArticleId,
            row.Keyword, row.Run.Status.ToString(), row.Run.Stage.ToString(), row.Done, row.Total, row.Error,
            row.Run.Warning, row.Run.StopRequested, BulkGenerationWorkflow.Settings(row.Run).Options, row.Title)).ToArray();
    }

    public async Task<ArticleServiceResult> RetryAsync(ArticleActor actor, Guid articleId, CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var run = await LockRunAsync(actor, articleId, cancellationToken);
        if (run is null) return ArticleServiceResult.Failure(ArticleServiceError.NotFound);
        if (run.Status is not (JobStatus.Failed or JobStatus.Canceled))
            return ArticleServiceResult.Failure(ArticleServiceError.ConflictRunningJob);
        var job = await db.ArticleGenerationJobs.SingleAsync(j => j.GenerationRunId == run.Id && j.JobType == run.Stage, cancellationToken);
        run.StopRequested = false;
        run.FinishedAt = null;
        if (job.Status == JobStatus.Succeeded)
        {
            await db.SaveChangesAsync(cancellationToken);
            await workflow.AdvanceAsync(job, cancellationToken);
        }
        else
        {
            job.Status = JobStatus.Queued;
            job.AttemptCount = 0;
            job.NextRunAt = null;
            job.FinishedAt = null;
            job.CanceledAt = null;
            job.ErrorCode = null;
            job.ErrorMessage = null;
            run.Status = JobStatus.Queued;
            var article = await db.Articles.SingleAsync(a => a.Id == articleId, cancellationToken);
            article.Status = job.JobType == JobType.BodyGeneration ? ArticleStatus.BodyQueued : ArticleStatus.OutlineQueued;
        }
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ArticleServiceResult.Success;
    }

    public async Task<ArticleServiceResult> StopAsync(ArticleActor actor, Guid articleId, CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var run = await LockRunAsync(actor, articleId, cancellationToken);
        if (run is null) return ArticleServiceResult.Failure(ArticleServiceError.NotFound);
        if (run.Status is not (JobStatus.Queued or JobStatus.Running))
            return ArticleServiceResult.Failure(ArticleServiceError.ConflictRunningJob);
        run.StopRequested = true;
        var canceled = await db.ArticleGenerationJobs.Where(j => j.GenerationRunId == run.Id && j.Status == JobStatus.Queued)
            .ExecuteUpdateAsync(setters => setters.SetProperty(j => j.Status, JobStatus.Canceled)
                .SetProperty(j => j.CanceledAt, DateTimeOffset.UtcNow), cancellationToken);
        if (canceled > 0)
        {
            run.Status = JobStatus.Canceled;
            run.FinishedAt = DateTimeOffset.UtcNow;
            await workflow.SetStoppedArticleStateAsync(articleId, cancellationToken);
        }
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ArticleServiceResult.Success;
    }

    private async Task<ArticleGenerationRun?> LockRunAsync(ArticleActor actor, Guid articleId, CancellationToken cancellationToken)
    {
        if (!await db.Articles.AnyAsync(a => a.Id == articleId && (a.UserId == actor.UserId || actor.IsAdmin), cancellationToken)) return null;
        // Acquire the stage job before the run, matching the worker's lock order.
        while (true)
        {
            var snapshot = await db.ArticleGenerationRuns.AsNoTracking().SingleOrDefaultAsync(r => r.ArticleId == articleId, cancellationToken);
            if (snapshot is null) return null;
            var stage = snapshot.Stage.ToString();
            var job = await db.ArticleGenerationJobs.FromSqlInterpolated(
                $"SELECT * FROM \"ArticleGenerationJobs\" WHERE \"GenerationRunId\" = {snapshot.Id} AND \"JobType\" = {stage} FOR UPDATE")
                .SingleAsync(cancellationToken);
            await db.Entry(job).ReloadAsync(cancellationToken);
            var run = await db.ArticleGenerationRuns.SingleAsync(r => r.Id == snapshot.Id, cancellationToken);
            await db.Entry(run).ReloadAsync(cancellationToken);
            if (run.Stage == snapshot.Stage) return run;
        }
    }
}
