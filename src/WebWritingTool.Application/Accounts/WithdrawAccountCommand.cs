namespace WebWritingTool.Application.Accounts;

public sealed record WithdrawAccountCommand(
    string UserId,
    string CurrentPassword,
    string ConfirmText);
