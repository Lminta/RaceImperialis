const configuredDialogs = new WeakSet();

export function open(dialog) {
    if (!configuredDialogs.has(dialog)) {
        configuredDialogs.add(dialog);
        dialog.addEventListener('keydown', event => {
            if (event.key !== 'Tab') return;
            const controls = Array.from(dialog.querySelectorAll('button, input, select, textarea, [tabindex]'))
                .filter(element => !element.matches(':disabled') && element.tabIndex >= 0 && element.getClientRects().length);
            if (!controls.length) return;
            event.preventDefault();
            const current = controls.indexOf(document.activeElement);
            const next = (current + (event.shiftKey ? -1 : 1) + controls.length) % controls.length;
            controls[next].focus();
        });
    }
    if (!dialog.open) dialog.showModal();
}

export function close(dialog) {
    dialog.close();
}
