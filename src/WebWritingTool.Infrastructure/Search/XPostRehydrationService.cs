using Microsoft.EntityFrameworkCore;
using WebWritingTool.Application.Search;
using WebWritingTool.Domain.Search;
using WebWritingTool.Infrastructure.Data;

namespace WebWritingTool.Infrastructure.Search;

public sealed class XPostRehydrationService(
    ApplicationDbContext dbContext,
    IXFullArchiveSearchClient xClient,
    SearchCachePolicyResolver cachePolicyResolver)
    : IXPostRehydrationService
{
    private const int MaxPostIdsPerRequest = 100;

    public async Task<XPostRehydrationServiceResult> RehydrateCachedPostsAsync(
        string userId,
        IReadOnlyList<string> postIds,
        TopicRiskMode topicRiskMode,
        CancellationToken cancellationToken = default)
    {
        var normalizedIds = postIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (normalizedIds.Length == 0)
        {
            return new XPostRehydrationServiceResult(
                RehydrationRequired: false,
                RequestedCount: 0,
                RefreshedCount: 0,
                MissingCount: 0,
                ChangedCount: 0);
        }

        var rehydrationRequired = cachePolicyResolver.RequiresXRehydrationBeforeDisplay(topicRiskMode);
        if (!rehydrationRequired)
        {
            return new XPostRehydrationServiceResult(
                RehydrationRequired: false,
                RequestedCount: normalizedIds.Length,
                RefreshedCount: 0,
                MissingCount: 0,
                ChangedCount: 0);
        }

        var cachedPosts = await dbContext.XSearchPosts
            .Where(post => post.UserId == userId && normalizedIds.Contains(post.PostId))
            .ToListAsync(cancellationToken);
        var freshById = new Dictionary<string, XSearchPostResult>(StringComparer.Ordinal);
        foreach (var batch in normalizedIds.Chunk(MaxPostIdsPerRequest))
        {
            var freshPosts = await xClient.RehydrateAsync(
                new XPostRehydrationRequest(batch),
                cancellationToken);
            foreach (var freshPost in freshPosts)
            {
                freshById[freshPost.PostId] = freshPost;
            }
        }

        var now = DateTimeOffset.UtcNow;
        var ttl = cachePolicyResolver.ResolveX(now, topicRiskMode);
        var refreshedCount = 0;
        var missingCount = 0;
        var changedCount = 0;

        foreach (var cachedPost in cachedPosts)
        {
            if (freshById.TryGetValue(cachedPost.PostId, out var fresh))
            {
                if (HasChanged(cachedPost, fresh))
                {
                    changedCount++;
                }

                cachedPost.AuthorId = fresh.AuthorId;
                cachedPost.Text = fresh.Text;
                cachedPost.Url = fresh.Url;
                cachedPost.Language = fresh.Language;
                cachedPost.PostedAt = fresh.PostedAt;
                cachedPost.FetchedAt = now;
                cachedPost.CacheExpiresAt = ttl.CacheExpiresAt;
                cachedPost.ContentExpiresAt = ttl.ContentExpiresAt;
                cachedPost.MetadataExpiresAt ??= ttl.MetadataExpiresAt;
                refreshedCount++;
            }
            else
            {
                cachedPost.Text = null;
                cachedPost.FetchedAt = now;
                cachedPost.CacheExpiresAt = now;
                cachedPost.ContentExpiresAt = now;
                missingCount++;
            }
        }

        if (cachedPosts.Count > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return new XPostRehydrationServiceResult(
            RehydrationRequired: true,
            RequestedCount: normalizedIds.Length,
            RefreshedCount: refreshedCount,
            MissingCount: missingCount + Math.Max(0, normalizedIds.Length - cachedPosts.Count),
            ChangedCount: changedCount);
    }

    private static bool HasChanged(XSearchPost cachedPost, XSearchPostResult freshPost)
    {
        return !string.Equals(cachedPost.AuthorId, freshPost.AuthorId, StringComparison.Ordinal)
            || !string.Equals(cachedPost.Text, freshPost.Text, StringComparison.Ordinal)
            || !string.Equals(cachedPost.Url, freshPost.Url, StringComparison.Ordinal)
            || !string.Equals(cachedPost.Language, freshPost.Language, StringComparison.Ordinal)
            || cachedPost.PostedAt != freshPost.PostedAt;
    }
}
