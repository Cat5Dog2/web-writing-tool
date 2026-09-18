using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WebWritingTool.Application.Security;
using WebWritingTool.Infrastructure.Data;

namespace WebWritingTool.Infrastructure.BackgroundJobs;

public sealed class JobDispatcher(IServiceScopeFactory scopeFactory)
{
    public async Task<JobExecutionResult> DispatchAsync(
        LeasedJob job,
        CancellationToken cancellationToken = default)
    {
        // Cookieのないジョブでも、永続化された所有者のゲスト属性で接続先を決定する。
        // ジョブごとにスコープを分け、同じWorker内の通常ユーザーへモードを持ち越さない。
        await using var scope = scopeFactory.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var dbContext = services.GetRequiredService<ApplicationDbContext>();
        if (await dbContext.UserClaims.AnyAsync(claim => claim.UserId == job.UserId
            && claim.ClaimType == GuestIdentity.ClaimType && claim.ClaimValue == GuestIdentity.ClaimValue,
            cancellationToken))
        {
            services.GetRequiredService<ExternalApiExecutionContext>().EnableGuestMode();
        }

        var handler = services.GetServices<IJobHandler>().FirstOrDefault(item => item.JobType == job.JobType);
        if (handler is null)
        {
            throw new JobExecutionException(
                JobErrorCodes.Conflict,
                "このジョブ種別の処理はまだ実装されていません。");
        }

        return await handler.HandleAsync(job, cancellationToken);
    }
}
