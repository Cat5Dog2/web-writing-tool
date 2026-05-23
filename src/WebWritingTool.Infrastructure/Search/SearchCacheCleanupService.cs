using Microsoft.EntityFrameworkCore;
using WebWritingTool.Infrastructure.Data;

namespace WebWritingTool.Infrastructure.Search;

public sealed class SearchCacheCleanupService(ApplicationDbContext dbContext)
{
    public async Task<SearchCacheCleanupResult> CleanupExpiredAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var expiredSearchMetadata = await dbContext.SearchResults
            .Where(result => result.MetadataExpiresAt != null && result.MetadataExpiresAt <= now)
            .ToListAsync(cancellationToken);

        var expiredSearchContent = await dbContext.SearchResults
            .Where(result => (result.MetadataExpiresAt == null || result.MetadataExpiresAt > now)
                && result.ContentExpiresAt != null
                && result.ContentExpiresAt <= now
                && result.Snippet != null)
            .ToListAsync(cancellationToken);

        var expiredXMetadata = await dbContext.XSearchPosts
            .Where(post => post.MetadataExpiresAt != null && post.MetadataExpiresAt <= now)
            .ToListAsync(cancellationToken);

        var expiredXContent = await dbContext.XSearchPosts
            .Where(post => (post.MetadataExpiresAt == null || post.MetadataExpiresAt > now)
                && post.ContentExpiresAt != null
                && post.ContentExpiresAt <= now
                && post.Text != null)
            .ToListAsync(cancellationToken);

        foreach (var result in expiredSearchContent)
        {
            result.Snippet = null;
        }

        foreach (var post in expiredXContent)
        {
            post.Text = null;
        }

        dbContext.SearchResults.RemoveRange(expiredSearchMetadata);
        dbContext.XSearchPosts.RemoveRange(expiredXMetadata);

        var changed =
            expiredSearchContent.Count
            + expiredXContent.Count
            + expiredSearchMetadata.Count
            + expiredXMetadata.Count;

        if (changed > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return new SearchCacheCleanupResult(
            expiredSearchContent.Count,
            expiredSearchMetadata.Count,
            expiredXContent.Count,
            expiredXMetadata.Count);
    }
}

public sealed record SearchCacheCleanupResult(
    int SearchResultsContentCleared,
    int SearchResultsDeleted,
    int XSearchPostsContentCleared,
    int XSearchPostsDeleted)
{
    public int TotalChanged =>
        SearchResultsContentCleared
        + SearchResultsDeleted
        + XSearchPostsContentCleared
        + XSearchPostsDeleted;
}
