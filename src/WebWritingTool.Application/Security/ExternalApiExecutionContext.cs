namespace WebWritingTool.Application.Security;

// HTTPリクエスト、Blazor circuit、ジョブごとのスコープで共有する。
public sealed class ExternalApiExecutionContext
{
    public bool IsGuest { get; private set; }

    public void EnableGuestMode() => IsGuest = true;
}
