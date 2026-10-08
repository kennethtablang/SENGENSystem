import { useRef } from 'react';
import { useModalFocus } from './useModalFocus';

/* The visual half of `confirmAction`. Split out of confirm.jsx so that file exports only the
   imperative helpers — a module mixing components with plain functions breaks Fast Refresh, which
   is what `react-refresh/only-export-components` was telling us. */

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

export default function ConfirmDialog({ title, message, confirmLabel, cancelLabel, danger, onDone }) {
    const cancelRef = useRef(null);

    /* `role="alertdialog"` rather than `dialog`, and Cancel focused first: this interrupts to ask
       about something destructive, so the safe answer should be the one already under the user's
       finger when they hit Enter by reflex. The shared hook adds the focus trap and restores focus
       to whatever triggered the confirmation. */
    const dialogRef = useModalFocus({ onEscape: () => onDone(false) });

    return (
        <div className="modal-overlay" onClick={() => onDone(false)} role="presentation">
            <div
                ref={dialogRef}
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
