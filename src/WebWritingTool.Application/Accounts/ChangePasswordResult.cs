namespace WebWritingTool.Application.Accounts;

public sealed record ChangePasswordResult(ChangePasswordError Error)
{
    public static ChangePasswordResult Success { get; } = new(ChangePasswordError.None);

    public bool Succeeded => Error == ChangePasswordError.None;
}
