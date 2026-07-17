import { useEffect, useRef } from 'react';
import { createRoot } from 'react-dom/client';
import { confirmsDeletes } from '../settings/prefs';

// Styled, promise-based replacement for window.confirm — the second thought before
// anything destructive (deletes) or session-ending (sign out) happens:
//
//   if (!(await confirmAction({ title: 'Delete room?', message: '…', danger: true }))) return;
//
// Renders into its own root on document.body, resolves true/false, and cleans up after
// itself, so call sites need no modal state or extra JSX.

function DangerGlyph() {
    return (
        <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor"
            strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
            <path d="M3 6h18M8 6V4a1 1 0 0 1 1-1h6a1 1 0 0 1 1 1v2m3 0-1 14a2 2 0 0 1-2 2H8a2 2 0 0 1-2-2L5 6M10 11v6M14 11v6" />
        </svg>
    );
}

function QuestionGlyph() {
    return (
        <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor"
            strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
            <path d="M12 22a10 10 0 1 0 0-20 10 10 0 0 0 0 20M9.1 9a3 3 0 0 1 5.83 1c0 2-3 3-3 3M12 17h.01" />
        </svg>
    );
}

function ConfirmDialog({ title, message, confirmLabel, cancelLabel, danger, onDone }) {
    const cancelRef = useRef(null);

    useEffect(() => {
        cancelRef.current?.focus();
        const onKey = e => { if (e.key === 'Escape') onDone(false); };
        window.addEventListener('keydown', onKey);
        document.body.style.overflow = 'hidden';
        return () => {
            window.removeEventListener('keydown', onKey);
            document.body.style.overflow = '';
        };
    }, [onDone]);

    return (
        <div className="modal-overlay" onClick={() => onDone(false)} role="presentation">
            <div
                className="modal modal-confirm"
                role="alertdialog"
                aria-modal="true"
                aria-label={title}
                onClick={e => e.stopPropagation()}
            >
                <div className="modal-body">
                    <div className="confirm-body">
                        <span className={`confirm-icon${danger ? ' is-danger' : ''}`} aria-hidden="true">
                            {danger ? <DangerGlyph /> : <QuestionGlyph />}
                        </span>
                        <div>
                            <p className="confirm-title">{title}</p>
                            <p className="confirm-msg">{message}</p>
                        </div>
                    </div>
                </div>
                <footer className="modal-foot">
                    <button type="button" className="btn btn-ghost" ref={cancelRef} onClick={() => onDone(false)}>
                        {cancelLabel}
                    </button>
                    <button
                        type="button"
                        className={`btn ${danger ? 'btn-danger' : 'btn-primary'}`}
                        onClick={() => onDone(true)}
                    >
                        {confirmLabel}
                    </button>
                </footer>
            </div>
        </div>
    );
}

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
