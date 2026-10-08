import { createRoot } from 'react-dom/client';
import ConfirmDialog from './ConfirmDialog';
import { confirmsDeletes } from '../settings/prefs';

// Styled, promise-based replacement for window.confirm — the second thought before
// anything destructive (deletes) or session-ending (sign out) happens:
//
//   if (!(await confirmAction({ title: 'Delete room?', message: '…', danger: true }))) return;
//
// Renders into its own root on document.body, resolves true/false, and cleans up after
// itself, so call sites need no modal state or extra JSX.
//
// The dialog itself lives in ConfirmDialog.jsx: a module that exports both components and plain
// functions breaks Fast Refresh, so this file exports only the two helpers below.

export function confirmAction({
    title = 'Are you sure?',
    message = '',
    confirmLabel = 'Confirm',
    cancelLabel = 'Cancel',
    danger = false
} = {}) {
    return new Promise(resolve => {
        const host = document.createElement('div');
        document.body.appendChild(host);
        const root = createRoot(host);
        const onDone = result => {
            // Unmount outside the render cycle React is currently flushing.
            setTimeout(() => { root.unmount(); host.remove(); }, 0);
            resolve(result);
        };
        root.render(
            <ConfirmDialog
                title={title}
                message={message}
                confirmLabel={confirmLabel}
                cancelLabel={cancelLabel}
                danger={danger}
                onDone={onDone}
            />
        );
    });
}

// Preset for the delete pattern used across the setup/CRUD pages. Honors the
// "Confirm before deleting" preference — when it's off, deletes go straight through.
export function confirmDelete(what, detail) {
    if (!confirmsDeletes()) return Promise.resolve(true);
    return confirmAction({
        title: `Delete ${what}?`,
        message: detail ?? 'This can’t be undone.',
        confirmLabel: 'Delete',
        danger: true
    });
}
