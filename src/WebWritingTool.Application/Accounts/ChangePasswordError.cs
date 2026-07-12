namespace WebWritingTool.Application.Accounts;

public enum ChangePasswordError
{
    None,
    UserNotFound,
    InvalidCurrentPassword,
    NewPasswordMismatch,
    NewPasswordSameAsCurrent,
    InvalidNewPassword,
    ChangeFailed
}
