namespace WebWritingTool.Application.Security;

public static class ApplicationPolicies
{
    public const string RequireAdmin = "RequireAdmin";
    public const string AdminManageUser = "AdminManageUser";
    public const string AdminDeleteUser = "AdminDeleteUser";
    public const string SelfWithdrawAccount = "SelfWithdrawAccount";
    public const string OwnArticle = "OwnArticle";
    public const string OwnWordpressSite = "OwnWordpressSite";
    public const string OwnNotificationSetting = "OwnNotificationSetting";
    public const string OwnJob = "OwnJob";
}
