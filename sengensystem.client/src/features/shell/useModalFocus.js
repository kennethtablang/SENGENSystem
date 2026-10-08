import { useEffect, useRef } from 'react';

/* Keeps keyboard focus inside an open dialog, and puts it back where it was on close.

   Without this a modal is only visually modal. Tab from the last control and focus walks out into
   the page behind the overlay — which is still there, still interactive to a keyboard, and now
   completely invisible to the person operating it. A screen-reader or keyboard-only user ends up
   filling in a form they cannot see while a dialog they cannot reach sits on top.

   `aria-modal="true"` (already on every dialog here) tells assistive tech to ignore the background,
   but it does not move focus and it does not stop Tab. Both still have to be done in script.

   Usage:
     const dialogRef = useModalFocus({ onEscape: onClose, enabled: !busy });
     ...
     <div className="modal" ref={dialogRef} role="dialog" aria-modal="true"> */

const FOCUSABLE = [
    'a[href]', 'button:not([disabled])', 'input:not([disabled])',
    'select:not([disabled])', 'textarea:not([disabled])',
    '[tabindex]:not([tabindex="-1"])'
].join(',');

export function useModalFocus({ onEscape, enabled = true } = {}) {
    const ref = useRef(null);
    /* Captured in a ref rather than state: it must survive every re-render of the dialog, and
       reading document.activeElement at close time would be too late — by then it is inside the
       dialog that is about to disappear. */
    const returnTo = useRef(null);

    useEffect(() => {
        returnTo.current = document.activeElement;
        const node = ref.current;

        /* Focus the first real control, not the dialog container. Landing on the close button is
           the common outcome and it is a reasonable one — it tells the user immediately how to get
           out, which is the first thing you want to know about a dialog you did not expect. */
        const focusables = node?.querySelectorAll(FOCUSABLE);
        if (focusables?.length) {
            focusables[0].focus();
        } else {
            node?.focus();
        }

        return () => {
            /* Put focus back on whatever opened the dialog — usually the row action or button the
               user pressed. Without this, closing drops focus to <body> and the next Tab starts
               again from the top of the page, losing their place entirely.
               Guarded because the opener may have been unmounted by the very action just taken
               (approving a request removes its row, and with it the button). */
            const target = returnTo.current;
            if (target && document.contains(target) && typeof target.focus === 'function') {
                target.focus();
            }
        };
    }, []);

    useEffect(() => {
        function onKeyDown(event) {
            if (event.key === 'Escape') {
                if (enabled) onEscape?.();
                return;
            }
            if (event.key !== 'Tab') return;

            const node = ref.current;
            if (!node) return;
            const focusables = [...node.querySelectorAll(FOCUSABLE)]
                // A control can match the selector while being invisible (a collapsed panel), and
                // focusing something with no layout box strands the user on nothing.
                .filter(el => el.offsetParent !== null || el === document.activeElement);
            if (focusables.length === 0) return;

            const first = focusables[0];
            const last = focusables[focusables.length - 1];

            // Wrap at both ends — this is the trap itself.
            if (event.shiftKey && document.activeElement === first) {
                event.preventDefault();
                last.focus();
            } else if (!event.shiftKey && document.activeElement === last) {
                event.preventDefault();
                first.focus();
            }
        }

        window.addEventListener('keydown', onKeyDown);
        return () => window.removeEventListener('keydown', onKeyDown);
    }, [onEscape, enabled]);

    return ref;
}
