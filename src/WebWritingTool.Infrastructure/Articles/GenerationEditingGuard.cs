using Microsoft.EntityFrameworkCore;
using WebWritingTool.Domain.Jobs;
using WebWritingTool.Infrastructure.Data;

namespace WebWritingTool.Infrastructure.Articles;

internal static class GenerationEditingGuard
{
    public static Task<bool> IsActiveAsync(ApplicationDbContext db, Guid articleId, CancellationToken cancellationToken) =>
        db.ArticleGenerationRuns.AnyAsync(r => r.ArticleId == articleId
            && (r.Status == JobStatus.Queued || r.Status == JobStatus.Running), cancellationToken);
}
