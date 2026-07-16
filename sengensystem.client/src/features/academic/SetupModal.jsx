import { useEffect } from 'react';
import { createPortal } from 'react-dom';

// Portaled modal shell reused by the academic-setup screens. Rendering into document.body
// keeps position:fixed centering correct — page content sits inside a `rise` transform that
// would otherwise become the containing block and trap the overlay (see users/registration).
export default function SetupModal({ title, onClose, children, footer, className }) {
    useEffect(() => {
        const onKey = (e) => { if (e.key === 'Escape') onClose(); };
        window.addEventListener('keydown', onKey);
        document.body.style.overflow = 'hidden';
        return () => { window.removeEventListener('keydown', onKey); document.body.style.overflow = ''; };
    }, [onClose]);

    return createPortal(
        <div className="modal-overlay" onClick={onClose} role="presentation">
            <div className={`modal${className ? ` ${className}` : ''}`} role="dialog" aria-modal="true" aria-label={title} onClick={e => e.stopPropagation()}>
                <header className="modal-head">
                    <h2>{title}</h2>
                    <button type="button" className="modal-close" onClick={onClose} aria-label="Close">×</button>
                </header>
                <div className="modal-body">{children}</div>
                {footer && <footer className="modal-foot">{footer}</footer>}
            </div>
        </div>,
        document.body
    );
}
