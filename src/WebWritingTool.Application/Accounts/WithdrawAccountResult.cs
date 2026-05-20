namespace WebWritingTool.Application.Accounts;

public sealed record WithdrawAccountResult(WithdrawAccountError Error)
{
    public static WithdrawAccountResult Success { get; } = new(WithdrawAccountError.None);

    public bool Succeeded => Error == WithdrawAccountError.None;
}
