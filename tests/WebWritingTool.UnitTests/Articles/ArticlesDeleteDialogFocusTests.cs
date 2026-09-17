using System.Reflection;
using Microsoft.JSInterop;
using WebWritingTool.Application.Articles;
using ArticlesPage = WebWritingTool.Web.Components.Pages.Articles;

namespace WebWritingTool.UnitTests.Articles;

public class ArticlesDeleteDialogFocusTests
{
    private const BindingFlags InstanceNonPublic = BindingFlags.Instance | BindingFlags.NonPublic;

    [Fact]
    public async Task OnAfterRenderAsync_WhenCancelledWhileModuleImportIsPending_DoesNotOpenOrMarkFocusActive()
    {
        var componentType = typeof(ArticlesPage);
        var component = new ArticlesPage();
        var deleteTargetField = componentType.GetField("deleteTarget", InstanceNonPublic)!;
        var runtime = new PendingImportRuntime();
        var module = new RecordingModule(() => deleteTargetField.GetValue(component) is null);
        componentType.GetProperty("JSRuntime", InstanceNonPublic)!.SetValue(component, runtime);

        ConfirmDelete(component, NewArticle());
        Task InvokeAfterRenderAsync() => InvokeOnAfterRenderAsync(component);

        var opening = InvokeAfterRenderAsync();
        Assert.False(opening.IsCompleted);
        Assert.Equal(1, runtime.PendingImportCount);

        // CancelDelete() and its render can run while the import above is still awaited -- nothing
        // blocks a button click from being dispatched during that await (precommit-review-2026-09-17
        // review). This reproduces that interleaving directly against the real component.
        CancelDelete(component);
        await InvokeAfterRenderAsync();

        runtime.CompleteImport(0, module);
        await opening;

        Assert.False(module.OpenCalledWhileClosed);
        Assert.False(GetDeleteDialogFocusActive(component));

        await component.DisposeAsync();
    }

    [Fact]
    public async Task OnAfterRenderAsync_WhenReopenedBeforePendingImportCompletes_SharesTheSameImportAndOnlyOpensOnce()
    {
        var componentType = typeof(ArticlesPage);
        var component = new ArticlesPage();
        var runtime = new PendingImportRuntime();
        var module = new RecordingModule();
        componentType.GetProperty("JSRuntime", InstanceNonPublic)!.SetValue(component, runtime);

        ConfirmDelete(component, NewArticle());
        Task InvokeAfterRenderAsync() => InvokeOnAfterRenderAsync(component);

        var openingFirst = InvokeAfterRenderAsync();
        Assert.False(openingFirst.IsCompleted);
        Assert.Equal(1, runtime.PendingImportCount);

        CancelDelete(component);
        await InvokeAfterRenderAsync();

        // Reopening before the first import resolves must await the SAME pending import rather
        // than starting a second, independent one -- two separate imports would each hand back a
        // distinct IJSObjectReference, and the field assignment only keeps whichever one resolves
        // last, leaking the other (delete-focus-followup-2026-09-18 review, second follow-up).
        ConfirmDelete(component, NewArticle());
        var openingSecond = InvokeAfterRenderAsync();
        Assert.False(openingSecond.IsCompleted);
        Assert.Equal(1, runtime.PendingImportCount);

        runtime.CompleteImport(0, module);
        await openingFirst;
        await openingSecond;

        Assert.Equal(1, module.OpenCalls);
        Assert.True(GetDeleteDialogFocusActive(component));

        await component.DisposeAsync();
    }

    [Fact]
    public async Task OnAfterRenderAsync_WhenCancelledThenDisposedWhileOpenIsPending_DoesNotThrowObjectDisposedException()
    {
        var componentType = typeof(ArticlesPage);
        var component = new ArticlesPage();
        var module = new RecordingModule();
        module.DelayOpens();
        componentType.GetProperty("JSRuntime", InstanceNonPublic)!.SetValue(component, new ImmediateImportRuntime(module));

        ConfirmDelete(component, NewArticle());
        Task InvokeAfterRenderAsync() => InvokeOnAfterRenderAsync(component);

        var opening = InvokeAfterRenderAsync();
        Assert.False(opening.IsCompleted);
        Assert.Equal(1, module.OpenCalls);

        CancelDelete(component);
        await InvokeAfterRenderAsync();

        // Disposal happens while "open" is still in flight -- the real JSObjectReference lets an
        // already-dispatched call finish normally, so it is the subsequent "close" call, not "open"
        // itself, that must not run against a disposed module (delete-focus-followup-2026-09-17 review).
        await component.DisposeAsync();
        module.CompleteOpen(0);

        await opening;

        Assert.Equal(0, module.CloseCalls);
        Assert.False(GetDeleteDialogFocusActive(component));
    }

    [Fact]
    public async Task OnAfterRenderAsync_WhenReopenedWhileOpenIsPending_DoesNotCloseTheNewerDialog()
    {
        var componentType = typeof(ArticlesPage);
        var component = new ArticlesPage();
        var module = new RecordingModule();
        module.DelayOpens();
        componentType.GetProperty("JSRuntime", InstanceNonPublic)!.SetValue(component, new ImmediateImportRuntime(module));

        ConfirmDelete(component, NewArticle());
        Task InvokeAfterRenderAsync() => InvokeOnAfterRenderAsync(component);

        var openingFirst = InvokeAfterRenderAsync();
        Assert.False(openingFirst.IsCompleted);
        Assert.Equal(1, module.OpenCalls);

        CancelDelete(component);
        await InvokeAfterRenderAsync();

        ConfirmDelete(component, NewArticle());
        var openingSecond = InvokeAfterRenderAsync();
        Assert.False(openingSecond.IsCompleted);
        Assert.Equal(2, module.OpenCalls);

        // The newer (second) dialog's own open() call resolves first and takes over the JS-side
        // focus trap.
        module.CompleteOpen(1);
        await openingSecond;
        Assert.True(GetDeleteDialogFocusActive(component));

        // The stale (first) dialog's open() call resolves afterward -- it must not close what the
        // newer dialog just opened, or the newer dialog's Escape/Tab handling silently breaks while
        // deleteDialogFocusActive still claims it is active (delete-focus-followup-2026-09-18 review).
        module.CompleteOpen(0);
        await openingFirst;

        Assert.Equal(0, module.CloseCalls);
        Assert.True(GetDeleteDialogFocusActive(component));

        await component.DisposeAsync();
    }

    [Fact]
    public async Task OnAfterRenderAsync_WhenDisposedWhileImportIsPending_ReleasesTheLateArrivingReference()
    {
        var componentType = typeof(ArticlesPage);
        var component = new ArticlesPage();
        var runtime = new PendingImportRuntime();
        var module = new RecordingModule();
        componentType.GetProperty("JSRuntime", InstanceNonPublic)!.SetValue(component, runtime);

        ConfirmDelete(component, NewArticle());
        var opening = InvokeOnAfterRenderAsync(component);
        Assert.False(opening.IsCompleted);
        Assert.Equal(1, runtime.PendingImportCount);

        // DisposeAsync runs while the import above is still pending, so dialogFocusModule is still
        // null and the component's own disposal has nothing to release yet.
        await component.DisposeAsync();
        Assert.Equal(0, module.DisposeCalls);

        // The import resolves afterward -- the reference it hands back must still be released
        // instead of being dropped by the disposed guard alone (delete-focus-followup-2026-09-18 review).
        runtime.CompleteImport(0, module);
        await opening;

        Assert.Equal(1, module.DisposeCalls);
        Assert.Equal(0, module.OpenCalls);
    }

    private static ArticleListItemResponse NewArticle() => new(
        Guid.NewGuid(), DateTimeOffset.UtcNow, "keyword", "Draft", "下書き",
        "Review article", "review", [], null, null, false, false, false);

    private static void ConfirmDelete(ArticlesPage component, ArticleListItemResponse article)
        => typeof(ArticlesPage).GetMethod("ConfirmDelete", InstanceNonPublic)!.Invoke(component, [article]);

    private static void CancelDelete(ArticlesPage component)
        => typeof(ArticlesPage).GetMethod("CancelDelete", InstanceNonPublic)!.Invoke(component, null);

    private static Task InvokeOnAfterRenderAsync(ArticlesPage component) => (Task)typeof(ArticlesPage)
        .GetMethod("OnAfterRenderAsync", InstanceNonPublic)!
        .Invoke(component, [false])!;

    private static bool GetDeleteDialogFocusActive(ArticlesPage component) =>
        (bool)typeof(ArticlesPage).GetField("deleteDialogFocusActive", InstanceNonPublic)!.GetValue(component)!;

    private sealed class PendingImportRuntime : IJSRuntime
    {
        private readonly List<TaskCompletionSource<IJSObjectReference>> pendingImports = [];

        public int PendingImportCount => pendingImports.Count;

        public void CompleteImport(int index, IJSObjectReference module) => pendingImports[index].SetResult(module);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
            => InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public async ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            if (identifier != "import")
            {
                throw new InvalidOperationException(identifier);
            }

            var completion = new TaskCompletionSource<IJSObjectReference>();
            pendingImports.Add(completion);
            return (TValue)await completion.Task;
        }
    }

    private sealed class ImmediateImportRuntime(IJSObjectReference module) : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
            => InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            if (identifier != "import")
            {
                throw new InvalidOperationException(identifier);
            }

            return ValueTask.FromResult((TValue)module);
        }
    }

    private sealed class RecordingModule(Func<bool>? isClosed = null) : IJSObjectReference
    {
        private readonly List<TaskCompletionSource> pendingOpens = [];
        private bool delayOpens;
        private bool disposed;

        public int OpenCalls { get; private set; }

        public int CloseCalls { get; private set; }

        public int DisposeCalls { get; private set; }

        public bool OpenCalledWhileClosed { get; private set; }

        public void DelayOpens() => delayOpens = true;

        public void CompleteOpen(int index) => pendingOpens[index].SetResult();

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
            => InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public async ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            // Mirrors the real JSObjectReference: disposal is only checked when a new call starts.
            // A call already dispatched before disposal still completes normally once its response
            // arrives -- it is the next call that throws (delete-focus-followup-2026-09-17 review).
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(RecordingModule));
            }

            if (identifier == "open")
            {
                OpenCalls++;
                OpenCalledWhileClosed = isClosed?.Invoke() ?? false;
                if (delayOpens)
                {
                    var completion = new TaskCompletionSource();
                    pendingOpens.Add(completion);
                    await completion.Task;
                }
            }
            else if (identifier == "close")
            {
                CloseCalls++;
            }
            else
            {
                throw new InvalidOperationException(identifier);
            }

            return default!;
        }

        public ValueTask DisposeAsync()
        {
            disposed = true;
            DisposeCalls++;
            return ValueTask.CompletedTask;
        }
    }
}
