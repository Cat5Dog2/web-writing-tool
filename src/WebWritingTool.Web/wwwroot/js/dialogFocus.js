let active = null;

function getFocusable(dialogElement) {
    return Array.from(
        dialogElement.querySelectorAll('button, [href], input, select, textarea, [tabindex]:not([tabindex="-1"])'))
        .filter((element) => !element.disabled && element.tabIndex !== -1);
}

function onKeydown(event) {
    if (!active) {
        return;
    }

    if (event.key === 'Escape') {
        event.preventDefault();
        active.dotNetRef.invokeMethodAsync('OnDeleteDialogEscape');
        return;
    }

    if (event.key !== 'Tab') {
        return;
    }

    const focusable = getFocusable(active.dialogElement);
    if (focusable.length === 0) {
        return;
    }

    const first = focusable[0];
    const last = focusable[focusable.length - 1];

    if (event.shiftKey && document.activeElement === first) {
        event.preventDefault();
        last.focus();
    } else if (!event.shiftKey && document.activeElement === last) {
        event.preventDefault();
        first.focus();
    }
}

export function open(dialogElement, initialFocusElement, dotNetRef) {
    if (!dialogElement || !dialogElement.isConnected) {
        return;
    }

    active = {
        dialogElement,
        previouslyFocused: document.activeElement,
        dotNetRef
    };

    dialogElement.addEventListener('keydown', onKeydown);
    (initialFocusElement ?? dialogElement).focus();
}

export function close() {
    if (!active) {
        return;
    }

    active.dialogElement.removeEventListener('keydown', onKeydown);
    active.previouslyFocused?.focus?.();
    active = null;
}
