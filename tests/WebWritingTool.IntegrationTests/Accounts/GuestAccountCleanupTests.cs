using System.Data.Common;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WebWritingTool.Application.Security;
using WebWritingTool.Domain.Articles;
using WebWritingTool.Domain.Jobs;
using WebWritingTool.Domain.Notifications;
using WebWritingTool.Domain.Search;
using WebWritingTool.Domain.Wordpress;
using WebWritingTool.Infrastructure.Data;
using WebWritingTool.Infrastructure.BackgroundJobs;
using WebWritingTool.Infrastructure.Identity;
using WebWritingTool.IntegrationTests.Support;
using WebWritingTool.Web.BackgroundJobs;
using WebWritingTool.Web.HealthChecks;

namespace WebWritingTool.IntegrationTests.Accounts;

[Collection(IntegrationTestCollection.Name)]
public class GuestAccountCleanupTests(IntegrationTestFixture fixture)
{
    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    public async Task CleanupExpiredAsync_AtEightHourBoundary_DeletesOnlyExpiredGuests(int secondsPastExpiry, bool deleted)
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        var userId = await CreateGuestAsync(now.AddHours(-8).AddSeconds(-secondsPastExpiry));
        using var scope = fixture.Factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<GuestAccountCleanupService>().CleanupExpiredAsync(now);
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(!deleted, await db.Users.AnyAsync(user => user.Id == userId));
    }

    [Fact]
    public async Task CleanupExpiredAsync_ExpiredGuest_RemovesOwnedDataAndIdentityButKeepsNormalUser()
    {
        var now = DateTimeOffset.UtcNow;
        var guestId = await CreateGuestAsync(now.AddHours(-9));
        var normalId = "guest-normal-" + Guid.NewGuid().ToString("N");
        await fixture.SeedUserAsync(normalId, $"{normalId}@example.test", ApplicationRoles.User);
        var guestArticle = await fixture.SeedArticleAsync(guestId, "期限切れゲストの記事");
        var normalArticle = await fixture.SeedArticleAsync(normalId, "通常ユーザーの記事");
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Users.Where(user => user.Id == normalId).ExecuteUpdateAsync(setters => setters
            .SetProperty(user => user.CreatedAt, now.AddDays(-1)));
        var parent = new ArticleHeading { ArticleId = guestArticle, Level = 2, Title = "親", Status = HeadingStatus.Pending };
        db.ArticleHeadings.AddRange(parent, new ArticleHeading
        {
            ArticleId = guestArticle,
            ParentId = parent.Id,
            Level = 3,
            Title = "子",
            Status = HeadingStatus.Pending
        });
        db.SearchResults.Add(new SearchResult
        {
            UserId = guestId,
            ArticleId = guestArticle,
            Query = "家庭菜園",
            Url = "https://example.test",
            IsDummy = true,
            FetchedAt = now
        });
        db.ArticleGenerationJobs.Add(new ArticleGenerationJob
        {
            UserId = guestId,
            ArticleId = guestArticle,
            JobType = JobType.BodyGeneration,
            Status = JobStatus.Queued,
            QueuedAt = now
        });
        db.ArticleGenerationRuns.Add(new ArticleGenerationRun
        {
            UserId = guestId,
            ArticleId = guestArticle,
            BatchId = Guid.NewGuid(),
            Stage = JobType.BodyGeneration,
            CreatedAt = now
        });
        db.WordpressSites.Add(new WordpressSite
        {
            UserId = guestId,
            SiteName = "テスト",
            BaseUrl = "https://example.test",
            LoginId = "guest",
            EncryptedApplicationPassword = "encrypted-test-value",
            DeletedAt = now
        });
        db.NotificationSettings.Add(new NotificationSetting
        {
            UserId = guestId,
            Provider = "Discord",
            DestinationMasked = "masked-test",
            EncryptedWebhookUrl = "encrypted-test-value"
        });
        await db.SaveChangesAsync();
        await db.Articles.Where(article => article.Id == guestArticle).ExecuteUpdateAsync(setters => setters
            .SetProperty(article => article.DeletedAt, now));

        var cleanup = scope.ServiceProvider.GetRequiredService<GuestAccountCleanupService>();
        await cleanup.CleanupExpiredAsync(now);

        Assert.False(await db.Users.AnyAsync(user => user.Id == guestId));
        Assert.False(await db.UserClaims.AnyAsync(claim => claim.UserId == guestId));
        Assert.False(await db.UserRoles.AnyAsync(role => role.UserId == guestId));
        Assert.False(await db.Articles.IgnoreQueryFilters().AnyAsync(article => article.Id == guestArticle));
        Assert.False(await db.ArticleHeadings.IgnoreQueryFilters().AnyAsync(heading => heading.ArticleId == guestArticle));
        Assert.False(await db.SearchResults.AnyAsync(result => result.UserId == guestId));
        Assert.False(await db.ArticleGenerationJobs.AnyAsync(job => job.UserId == guestId));
        Assert.False(await db.ArticleGenerationRuns.AnyAsync(run => run.UserId == guestId));
        Assert.False(await db.WordpressSites.IgnoreQueryFilters().AnyAsync(site => site.UserId == guestId));
        Assert.False(await db.NotificationSettings.IgnoreQueryFilters().AnyAsync(setting => setting.UserId == guestId));
        Assert.True(await db.Users.AnyAsync(user => user.Id == normalId));
        Assert.True(await db.Articles.AnyAsync(article => article.Id == normalArticle));
        Assert.Equal(0, await cleanup.CleanupExpiredAsync(now));
    }

    [Fact]
    public async Task CleanupExpiredAsync_RunningJob_DefersDeletionUntilJobFinishes()
    {
        var now = DateTimeOffset.UtcNow;
        var guestId = await CreateGuestAsync(now.AddHours(-9));
        var articleId = await fixture.SeedArticleAsync(guestId, "実行中のゲスト記事");
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var job = new ArticleGenerationJob
        {
            UserId = guestId,
            ArticleId = articleId,
            JobType = JobType.BodyGeneration,
            Status = JobStatus.Running,
            QueuedAt = now,
            LockedAt = now
        };
        db.ArticleGenerationJobs.Add(job);
        await db.SaveChangesAsync();
        var cleanup = scope.ServiceProvider.GetRequiredService<GuestAccountCleanupService>();
        await cleanup.CleanupExpiredAsync(now);
        Assert.True(await db.Users.AnyAsync(user => user.Id == guestId));
        Assert.True(await db.Articles.AnyAsync(article => article.Id == articleId));

        await db.ArticleGenerationJobs.Where(item => item.Id == job.Id).ExecuteUpdateAsync(setters => setters
            .SetProperty(item => item.Status, JobStatus.Succeeded));
        await cleanup.CleanupExpiredAsync(now.AddMinutes(1));
        Assert.False(await db.Users.AnyAsync(user => user.Id == guestId));
        Assert.False(await db.Articles.AnyAsync(article => article.Id == articleId));
    }

    [Fact]
    public async Task CleanupExpiredAsync_GuestPromotedToAdmin_DoesNotDeleteAdmin()
    {
        var now = DateTimeOffset.UtcNow;
        var userId = await CreateGuestAsync(now.AddHours(-9));
        using var scope = fixture.Factory.Services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await manager.FindByIdAsync(userId);
        Assert.NotNull(user);
        Assert.True((await manager.AddToRoleAsync(user, ApplicationRoles.Admin)).Succeeded);
        await scope.ServiceProvider.GetRequiredService<GuestAccountCleanupService>().CleanupExpiredAsync(now);
        Assert.NotNull(await manager.FindByIdAsync(userId));
    }

    private async Task<string> CreateGuestAsync(DateTimeOffset createdAt)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var guest = await scope.ServiceProvider.GetRequiredService<GuestAccountService>().CreateAsync();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Users.Where(user => user.Id == guest.Id).ExecuteUpdateAsync(setters => setters
            .SetProperty(user => user.CreatedAt, createdAt));
        return guest.Id;
    }

    [Fact]
    public async Task CleanupExpiredAsync_IdentityDeletionFails_RollsBackOwnedDataDeletion()
    {
        var now = DateTimeOffset.UtcNow;
        var userId = await CreateGuestAsync(now.AddHours(-9));
        var articleId = await fixture.SeedArticleAsync(userId, "削除失敗時に残す記事");
        using var factory = fixture.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddDbContext<ApplicationDbContext>(options => options.AddInterceptors(new RejectUserDeletion(userId)))));
        using var scope = factory.Services.CreateScope();
        var cleanup = scope.ServiceProvider.GetRequiredService<GuestAccountCleanupService>();

        var failure = await Assert.ThrowsAsync<DbUpdateException>(() => cleanup.CleanupExpiredAsync(now));
        Assert.IsType<InvalidOperationException>(failure.InnerException);

        using var verification = fixture.Factory.Services.CreateScope();
        var db = verification.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.True(await db.Users.AnyAsync(user => user.Id == userId));
        Assert.True(await db.Articles.AnyAsync(article => article.Id == articleId));
        Assert.True(await db.UserClaims.AnyAsync(claim => claim.UserId == userId));
    }

    [Fact]
    public async Task CleanupWorker_OnStartup_RemovesGuestsThatExpiredWhileApplicationWasStopped()
    {
        var userId = await CreateGuestAsync(DateTimeOffset.UtcNow.AddHours(-9));
        using var scope = fixture.Factory.Services.CreateScope();
        var logger = new CleanupCompletionLogger();
        var health = new BackgroundWorkerHealthState();
        using var worker = ActivatorUtilities.CreateInstance<GuestAccountCleanupWorker>(scope.ServiceProvider,
            Options.Create(new BackgroundJobOptions { Enabled = true }), logger, health);
        await worker.StartAsync(CancellationToken.None);
        try
        {
            await logger.Completed.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.False(await db.Users.AnyAsync(user => user.Id == userId));
            Assert.Equal(BackgroundWorkerState.Running, health.GetSnapshot(nameof(GuestAccountCleanupWorker)).State);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
        Assert.Equal(BackgroundWorkerState.Stopped, health.GetSnapshot(nameof(GuestAccountCleanupWorker)).State);
    }

    [Fact]
    public async Task CleanupExpiredAsync_JobBecomesRunningDuringCleanup_RechecksLockedJobAndKeepsData()
    {
        var now = DateTimeOffset.UtcNow;
        var userId = await CreateGuestAsync(now.AddHours(-9));
        var articleId = await fixture.SeedArticleAsync(userId, "削除と取得が競合する記事");
        using var jobScope = fixture.Factory.Services.CreateScope();
        var jobDb = jobScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var job = new ArticleGenerationJob
        {
            UserId = userId,
            ArticleId = articleId,
            JobType = JobType.BodyGeneration,
            Status = JobStatus.Queued,
            QueuedAt = now
        };
        jobDb.ArticleGenerationJobs.Add(job);
        await jobDb.SaveChangesAsync();
        await using var leaseTransaction = await jobDb.Database.BeginTransactionAsync();
        await jobDb.ArticleGenerationJobs.FromSqlInterpolated($"""
            SELECT * FROM "ArticleGenerationJobs" WHERE "Id" = {job.Id} FOR UPDATE
            """).ToListAsync();

        var observer = new JobLockObserver(userId);
        using var factory = fixture.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddDbContext<ApplicationDbContext>(options => options.AddInterceptors(observer))));
        using var cleanupScope = factory.Services.CreateScope();
        var cleanupTask = cleanupScope.ServiceProvider.GetRequiredService<GuestAccountCleanupService>().CleanupExpiredAsync(now);
        try
        {
            await observer.LockRequested.Task.WaitAsync(TimeSpan.FromSeconds(10));
            job.Status = JobStatus.Running;
            await jobDb.SaveChangesAsync();
        }
        finally
        {
            await leaseTransaction.CommitAsync();
        }
        await cleanupTask.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(await jobDb.Users.AnyAsync(user => user.Id == userId));
        Assert.True(await jobDb.Articles.AnyAsync(article => article.Id == articleId));
    }

    private sealed class JobLockObserver(string userId) : DbCommandInterceptor
    {
        public TaskCompletionSource LockRequested { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FROM \"ArticleGenerationJobs\" WHERE \"UserId\"", StringComparison.Ordinal)
                && command.CommandText.Contains("FOR UPDATE", StringComparison.Ordinal)
                && command.Parameters.Cast<DbParameter>().Any(parameter => Equals(parameter.Value, userId)))
            {
                LockRequested.TrySetResult();
            }
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private sealed class RejectUserDeletion(string userId) : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("DELETE FROM \"AspNetUsers\"", StringComparison.Ordinal)
                && command.Parameters.Cast<DbParameter>().Any(parameter => Equals(parameter.Value, userId)))
            {
                throw new InvalidOperationException("Simulated user deletion failure.");
            }
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private sealed class CleanupCompletionLogger : ILogger<GuestAccountCleanupWorker>
    {
        public TaskCompletionSource Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (formatter(state, exception).StartsWith("Expired guest account cleanup completed.", StringComparison.Ordinal))
            {
                Completed.TrySetResult();
            }
        }
    }
}
