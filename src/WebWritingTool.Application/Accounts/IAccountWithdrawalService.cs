namespace WebWritingTool.Application.Accounts;

public interface IAccountWithdrawalService
{
    Task<WithdrawAccountResult> WithdrawAsync(
        WithdrawAccountCommand command,
        CancellationToken cancellationToken = default);
}
