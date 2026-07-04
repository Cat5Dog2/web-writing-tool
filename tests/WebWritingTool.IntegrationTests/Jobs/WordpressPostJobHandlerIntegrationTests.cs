using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WebWritingTool.Application.Generation;
using WebWritingTool.Application.Rendering;
using WebWritingTool.Application.Search;
using WebWritingTool.Application.Security;
using WebWritingTool.Application.Wordpress;
using WebWritingTool.Domain.Articles;
using WebWritingTool.Domain.Jobs;
using WebWritingTool.Domain.Search;
using WebWritingTool.Domain.Wordpress;
using WebWritingTool.Infrastructure.BackgroundJobs;
using WebWritingTool.Infrastructure.BackgroundJobs.Handlers;
using WebWritingTool.Infrastructure.Data;
using WebWritingTool.IntegrationTests.Support;

namespace WebWritingTool.IntegrationTests.Jobs;

[Collection(IntegrationTestCollection.Name)]
public class WordpressPostJobHandlerIntegrationTests(IntegrationTestFixture fixture)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task HandleAsync_PublishWithXQuote_RehydratesOnlyPostIdsInOutgoingHtml()
    {
        const string quotedPostId = "1234567890";
        var context = await CreateContextAsync(
            $"""<blockquote cite="https://x.com/example/status/{quotedPostId}"><p>quoted</p></blockquote>""");

        using var scope = fixture.Factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.XSearchPosts.Add(new XSearchPost
        {
            UserId = context.UserId,
            ArticleId = context.ArticleId,
            Query = "unused",
            QueryHash = $"unused-{context.ArticleId:N}",
            PostId = "9999999999",
            Text = "unused",
            FetchedAt = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync();
        var rehydrationService = new RecordingRehydrationService(
            new XPostRehydrationServiceResult(true, 1, 1, 0, 0));
        var handler = CreateHandler(dbContext, rehydrationService);

        await handler.HandleAsync(context.LeasedJob);

        Assert.Equal([quotedPostId], Assert.Single(rehydrationService.Requests));
    }

    [Fact]
    public async Task HandleAsync_WhenQuotedPostChanged_BlocksPublishAndInvalidatesReview()
    {
        const string quotedPostId = "2234567890";
        var context = await CreateContextAsync(
            $"""<blockquote cite="https://twitter.com/example/status/{quotedPostId}"><p>old content</p></blockquote>""");

        using var scope = fixture.Factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var rehydrationService = new RecordingRehydrationService(
            new XPostRehydrationServiceResult(true, 1, 1, 0, 1));
        var wordpressClient = new RecordingWordpressClient();
        var handler = CreateHandler(dbContext, rehydrationService, wordpressClient);

        var exception = await Assert.ThrowsAsync<JobExecutionException>(
            () => handler.HandleAsync(context.LeasedJob));

        Assert.Equal(JobErrorCodes.XRehydrationFailed, exception.ErrorCode);
        Assert.Empty(wordpressClient.Requests);
        var article = await dbContext.Articles.SingleAsync(item => item.Id == context.ArticleId);
        Assert.Null(article.HumanReviewedAt);
        Assert.Null(article.HumanReviewedByUserId);
    }

    [Fact]
    public async Task HandleAsync_WhenXApiFails_RecordsFailureAndPreservesRetryMetadata()
    {
        const string quotedPostId = "3234567890";
        var context = await CreateContextAsync(
            $"""<blockquote cite="https://x.com/example/status/{quotedPostId}"><p>quoted</p></blockquote>""");
        var retryAfter = TimeSpan.FromMinutes(2);

        using var scope = fixture.Factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var rehydrationService = new RecordingRehydrationService(
            new ExternalIntegrationException(
                ExternalIntegrationErrorCodes.RateLimited,
                "X API rate limited.",
                retryAfter: retryAfter));
        var handler = CreateHandler(dbContext, rehydrationService);

        var exception = await Assert.ThrowsAsync<JobExecutionException>(
            () => handler.HandleAsync(context.LeasedJob));

        Assert.Equal(JobErrorCodes.RateLimited, exception.ErrorCode);
        Assert.Equal(retryAfter, exception.RetryAfter);
        var history = await dbContext.WordpressPosts.SingleAsync(post => post.JobId == context.LeasedJob.Id);
        Assert.Equal(WordpressPostStatus.Failed, history.Status);
        Assert.Equal(JobErrorCodes.RateLimited, history.ErrorCode);
    }

    private async Task<TestContext> CreateContextAsync(string htmlBody)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var userId = $"wordpress-handler-user-{suffix}";
        await fixture.SeedUserAsync(userId, $"{userId}@example.test", ApplicationRoles.User);

        using var scope = fixture.Factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var article = new Article
        {
            UserId = userId,
            Keyword = $"keyword-{suffix}",
            Title = $"title-{suffix}",
            Status = ArticleStatus.Completed,
            Tags = [],
            OutlineMethod = "Keyword",
            NotificationMode = "None",
            IsDomesticOnly = true,
            HtmlBody = htmlBody,
            HumanReviewRequired = true,
            HumanReviewedAt = DateTimeOffset.UtcNow,
            HumanReviewedByUserId = userId
        };
        var site = new WordpressSite
        {
            UserId = userId,
            SiteName = $"site-{suffix}",
            BaseUrl = "https://example.com",
            LoginId = "test-user",
            EncryptedApplicationPassword = "protected"
        };
        var job = new ArticleGenerationJob
        {
            UserId = userId,
            ArticleId = article.Id,
            JobType = JobType.WordpressPost,
            Status = JobStatus.Running,
            Priority = 0,
            Progress = 0,
            AttemptCount = 1,
            MaxAttempts = 3,
            QueuedAt = DateTimeOffset.UtcNow,
            StartedAt = DateTimeOffset.UtcNow,
            PayloadJson = JsonSerializer.Serialize(
                new WordpressPostPayload(
                    article.Id,
                    site.Id,
                    article.Title,
                    htmlBody,
                    null,
                    WordpressPostStatuses.Publish,
                    WordpressPostSources.Manual),
                JsonOptions)
        };
        dbContext.AddRange(article, site, job);
        dbContext.WordpressPosts.Add(new WordpressPost
        {
            UserId = userId,
            ArticleId = article.Id,
            WordpressSiteId = site.Id,
            JobId = job.Id,
            Title = article.Title,
            RequestedStatus = WordpressPostStatuses.Publish,
            Status = WordpressPostStatus.Queued,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync();

        return new TestContext(
            userId,
            article.Id,
            new LeasedJob(
                job.Id,
                userId,
                article.Id,
                null,
                JobType.WordpressPost,
                job.PayloadJson,
                1,
                3));
    }

    private static WordpressPostJobHandler CreateHandler(
        ApplicationDbContext dbContext,
        IXPostRehydrationService rehydrationService,
        RecordingWordpressClient? wordpressClient = null)
    {
        return new WordpressPostJobHandler(
            dbContext,
            wordpressClient ?? new RecordingWordpressClient(),
            new PassThroughSecretProtector(),
            new ContentRenderingService(),
            rehydrationService);
    }

    private sealed record TestContext(string UserId, Guid ArticleId, LeasedJob LeasedJob);

    private sealed class RecordingRehydrationService : IXPostRehydrationService
    {
        private readonly XPostRehydrationServiceResult? result;
        private readonly Exception? exception;

        public RecordingRehydrationService(XPostRehydrationServiceResult result)
        {
            this.result = result;
        }

        public RecordingRehydrationService(Exception exception)
        {
            this.exception = exception;
        }

        public List<IReadOnlyList<string>> Requests { get; } = [];

        public Task<XPostRehydrationServiceResult> RehydrateCachedPostsAsync(
            string userId,
            IReadOnlyList<string> postIds,
            TopicRiskMode topicRiskMode,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(postIds);
            if (exception is not null)
            {
                throw exception;
            }

            return Task.FromResult(result!);
        }
    }

    private sealed class RecordingWordpressClient : IWordpressClient
    {
        public List<WordpressPostRequest> Requests { get; } = [];

        public Task<WordpressConnectionTestResult> TestConnectionAsync(
            WordpressSiteConnection connection,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<WordpressCategoryDto>> GetCategoriesAsync(
            WordpressSiteConnection connection,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<WordpressPostResult> CreatePostAsync(
            WordpressPostRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(new WordpressPostResult(
                true,
                1,
                "https://example.com/post/1",
                null,
                null));
        }
    }

    private sealed class PassThroughSecretProtector : ISecretProtector
    {
        public string Protect(string plaintext) => plaintext;

        public string Unprotect(string protectedValue) => protectedValue;
    }
}
