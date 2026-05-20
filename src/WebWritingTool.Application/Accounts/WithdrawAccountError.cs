namespace WebWritingTool.Application.Accounts;

public enum WithdrawAccountError
{
    None,
    UserNotFound,
    InvalidPassword,
    InvalidConfirmationText,
    LastAdminUser,
    RunningJobExists,
    DeleteFailed
}
