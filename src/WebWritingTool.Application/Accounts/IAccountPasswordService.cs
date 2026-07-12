namespace WebWritingTool.Application.Accounts;

public interface IAccountPasswordService
{
    Task<ChangePasswordResult> ChangePasswordAsync(
        ChangePasswordCommand command,
        CancellationToken cancellationToken = default);
}
