const watches = new WeakMap();

export function watch(root, receiver) {
    stop(root);
    const viewport = window.visualViewport;
    const mobile = matchMedia('(max-width: 768px), (max-height: 430px)');
    const short = matchMedia('(max-height: 430px)');
    let frame = 0;
    let stopped = false;

    const update = () => {
        frame = 0;
        if (!root.isConnected) { stop(root); return; }
        const bar = root.querySelector('.article-editor-actions');
        const height = bar?.getBoundingClientRect().height ?? 0;
        root.style.setProperty('--editor-action-height', `${height}px`);
        // Do not treat pinch zoom as a keyboard or counteract the user's zoom/pan.
        const usable = mobile.matches && viewport && Math.abs(viewport.scale - 1) < .01;
        const inset = usable ? Math.max(0, innerHeight - viewport.height - viewport.offsetTop) : 0;
        root.style.setProperty('--editor-keyboard-inset', `${inset}px`);
        root.style.setProperty('--editor-visible-height', `${usable ? viewport.height : innerHeight}px`);
        root.classList.toggle('editor-keyboard-open', inset > 0);
        const input = document.activeElement;
        if (!usable || inset <= 0 || !root.contains(input) || !input.matches('input, textarea, select')) return;
        const rect = input.getBoundingClientRect();
        const top = viewport.offsetTop + 8;
        const bottom = viewport.offsetTop + viewport.height - height - 8;
        if (rect.bottom > bottom) window.scrollBy(0, rect.bottom - bottom);
        else if (rect.top < top) window.scrollBy(0, rect.top - top);
    };
    const schedule = () => {
        if (!stopped && !frame) frame = requestAnimationFrame(update);
    };
    const setShort = () => {
        receiver.invokeMethodAsync('SetShortViewportAsync', short.matches).catch(() => stop(root));
        schedule();
    };
    const sizeObserver = new ResizeObserver(schedule);
    sizeObserver.observe(root);
    // The action bar appears after article loading; observing the root also catches it.
    const contentObserver = new MutationObserver(schedule);
    contentObserver.observe(root, { childList: true, subtree: true });
    viewport?.addEventListener('resize', schedule);
    viewport?.addEventListener('scroll', schedule);
    window.addEventListener('resize', schedule);
    root.addEventListener('focusin', schedule);
    short.addEventListener('change', setShort);
    watches.set(root, () => {
        stopped = true;
        cancelAnimationFrame(frame);
        viewport?.removeEventListener('resize', schedule);
        viewport?.removeEventListener('scroll', schedule);
        window.removeEventListener('resize', schedule);
        root.removeEventListener('focusin', schedule);
        short.removeEventListener('change', setShort);
        sizeObserver.disconnect();
        contentObserver.disconnect();
    });
    setShort();
}

export function stop(root) {
    watches.get(root)?.();
    watches.delete(root);
}
