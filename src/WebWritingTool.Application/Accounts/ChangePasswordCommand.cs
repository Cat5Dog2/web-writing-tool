namespace WebWritingTool.Application.Accounts;

public sealed record ChangePasswordCommand(
    string UserId,
    string CurrentPassword,
    string NewPassword,
    string ConfirmNewPassword);
