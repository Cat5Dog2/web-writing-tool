const editors = new WeakMap();

export function watch(root, receiver) {
    const state = { dirty: false };
    state.input = event => {
        if (event.target.closest(".article-heading-editor, .article-editor-meta")) state.dirty = true;
    };
    state.click = event => {
        if (!state.dirty || event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
        const link = event.target.closest("a[href]");
        if (!link || link.download || (link.target && link.target !== "_self")) return;
        const url = new URL(link.href, location.href);
        if (url.origin !== location.origin || (url.pathname === location.pathname && url.search === location.search)) return;
        // Enhanced SSR navigation can bypass NavigationManager's location-changing handler.
        // Intercept before Blazor's link handler, including links in the shared sidebar.
        event.preventDefault();
        event.stopImmediatePropagation();
        receiver.invokeMethodAsync("ConfirmLinkNavigationAsync", url.href);
    };
    state.unload = event => {
        if (!state.dirty) return;
        event.preventDefault();
        event.returnValue = "";
    };
    root.addEventListener("input", state.input);
    root.addEventListener("change", state.input);
    document.addEventListener("click", state.click, true);
    window.addEventListener("beforeunload", state.unload);
    editors.set(root, state);
    state.observer = new MutationObserver(() => {
        if (!root.isConnected) stop(root);
    });
    state.observer.observe(document.body, { childList: true, subtree: true });
}

export function setDirty(root, dirty) {
    const state = editors.get(root);
    if (state) state.dirty = dirty;
}

export function stop(root) {
    const state = editors.get(root);
    if (!state) return;
    root.removeEventListener("input", state.input);
    root.removeEventListener("change", state.input);
    document.removeEventListener("click", state.click, true);
    window.removeEventListener("beforeunload", state.unload);
    state.observer.disconnect();
    editors.delete(root);
}
