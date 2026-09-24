const dialogs = new WeakMap();

export function open(dialog, receiver, returnFocusId) {
    const trigger = document.getElementById(returnFocusId) ?? document.activeElement;
    const cancel = event => {
        event.preventDefault();
        receiver.invokeMethodAsync("CloseAsync");
    };
    const keydown = event => {
        if (event.key !== "Tab") return;
        const items = [...dialog.querySelectorAll('button, a[href], input, select, textarea, [tabindex]')]
            .filter(element => !element.disabled && element.tabIndex >= 0 && element.getClientRects().length);
        if (!items.length) { event.preventDefault(); dialog.focus(); return; }
        const first = items[0];
        const last = items.at(-1);
        if (event.shiftKey && (document.activeElement === first || !items.includes(document.activeElement))) {
            event.preventDefault(); last.focus();
        } else if (!event.shiftKey && (document.activeElement === last || !items.includes(document.activeElement))) {
            event.preventDefault(); first.focus();
        }
    };
    dialogs.set(dialog, { trigger, cancel, keydown });
    dialog.addEventListener("cancel", cancel);
    dialog.addEventListener("keydown", keydown);
    dialog.showModal();
}

export function close(dialog) {
    const state = dialogs.get(dialog);
    if (!state) return;
    dialog.removeEventListener("cancel", state.cancel);
    dialog.removeEventListener("keydown", state.keydown);
    dialog.close();
    if (state.trigger?.isConnected) state.trigger.focus();
    dialogs.delete(dialog);
}

export function focus(id, preserveChildFocus = false) {
    const element = document.getElementById(id);
    // Delayed panel initialization must not interrupt an input the user already selected.
    if (preserveChildFocus && element?.contains(document.activeElement)) return;
    element?.focus();
    element?.scrollIntoView({ block: "nearest" });
}

export async function copy(text) {
    await navigator.clipboard.writeText(text);
}

export function focusInvalidField(id) {
    const element = document.getElementById(id);
    for (let parent = element?.parentElement; parent; parent = parent.parentElement) {
        if (parent.tagName === "DETAILS") parent.open = true;
    }
    element?.focus({ preventScroll: true });
    element?.scrollIntoView({ block: "center", behavior: "instant" });
}
