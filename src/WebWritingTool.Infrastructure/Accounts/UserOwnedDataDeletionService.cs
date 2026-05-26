using Microsoft.EntityFrameworkCore;
using WebWritingTool.Infrastructure.Data;

namespace WebWritingTool.Infrastructure.Accounts;

public sealed class UserOwnedDataDeletionService(ApplicationDbContext dbContext)
{
    public async Task<UserOwnedDataDeletionSummary> DeleteOwnedDataAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        var articleIds = await dbContext.Articles
            .IgnoreQueryFilters()
            .Where(article => article.UserId == userId)
            .Select(article => article.Id)
            .ToListAsync(cancellationToken);

        var notificationLogs = await dbContext.NotificationLogs
            .Where(log => log.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);
        var wordpressPosts = await dbContext.WordpressPosts
            .Where(post => post.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);
        var xSearchPosts = await dbContext.XSearchPosts
            .Where(post => post.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);
        var searchResults = await dbContext.SearchResults
            .Where(result => result.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);
        var usageLedgers = await dbContext.UsageLedgers
            .Where(ledger => ledger.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);
        var aiGenerationLogs = await dbContext.AiGenerationLogs
            .Where(log => log.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);
        var auditLogs = await dbContext.AuditLogs
            .Where(log => log.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);

        var jobs = await dbContext.ArticleGenerationJobs
            .Where(job => job.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);
        var childHeadings = await dbContext.ArticleHeadings
            .IgnoreQueryFilters()
            .Where(heading => heading.ParentId != null && articleIds.Contains(heading.ArticleId))
            .ExecuteDeleteAsync(cancellationToken);
        var remainingHeadings = await dbContext.ArticleHeadings
            .IgnoreQueryFilters()
            .Where(heading => articleIds.Contains(heading.ArticleId))
            .ExecuteDeleteAsync(cancellationToken);
        var articles = await dbContext.Articles
            .IgnoreQueryFilters()
            .Where(article => article.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);

        var wordpressSites = await dbContext.WordpressSites
            .IgnoreQueryFilters()
            .Where(site => site.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);
        var notificationSettings = await dbContext.NotificationSettings
            .IgnoreQueryFilters()
            .Where(setting => setting.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);
        var usageLimits = await dbContext.UserUsageLimits
            .Where(limit => limit.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);

        return new UserOwnedDataDeletionSummary(
            articles,
            childHeadings + remainingHeadings,
            jobs,
            aiGenerationLogs,
            usageLedgers,
            searchResults,
            xSearchPosts,
            wordpressSites,
            wordpressPosts,
            notificationSettings,
            notificationLogs,
            usageLimits,
            auditLogs);
    }
}

public sealed record UserOwnedDataDeletionSummary(
    int Articles,
    int ArticleHeadings,
    int ArticleGenerationJobs,
    int AiGenerationLogs,
    int UsageLedgers,
    int SearchResults,
    int XSearchPosts,
    int WordpressSites,
    int WordpressPosts,
    int NotificationSettings,
    int NotificationLogs,
    int UserUsageLimits,
    int AuditLogs)
{
    public int Total =>
        Articles
        + ArticleHeadings
        + ArticleGenerationJobs
        + AiGenerationLogs
        + UsageLedgers
        + SearchResults
        + XSearchPosts
        + WordpressSites
        + WordpressPosts
        + NotificationSettings
        + NotificationLogs
        + UserUsageLimits
        + AuditLogs;
}
