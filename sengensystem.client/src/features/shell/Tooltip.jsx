import { useCallback, useLayoutEffect, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import './tooltip.css';

/* A hover/focus tooltip for data. Wraps any element — HTML or SVG — and renders its
   content in a portal so it escapes card overflow and stacking contexts. Positioned
   against the anchor's box, flipping below when there is no room above and clamping
   to the viewport horizontally.

   Usage:
     <Tip content={<><b>42</b> seats free</>}>{...}</Tip>
     <Tip as="g" content={…}>{svgChildren}</Tip>   // inside an <svg>

   Keyboard users get the same content on focus, and the anchor carries the tooltip
   as an accessible description. */

const GAP = 10;       // px between the anchor and the bubble
const DELAY = 70;     // ms before opening, so sweeping the cursor stays quiet

export default function Tip({ content, children, as: Tag = 'span', className = '', ...rest }) {
    const anchorRef = useRef(null);
    const bubbleRef = useRef(null);
    const timerRef = useRef(null);
    const [open, setOpen] = useState(false);
    const [pos, setPos] = useState({ left: 0, top: 0, flipped: false });

    const show = useCallback(() => {
        clearTimeout(timerRef.current);
        timerRef.current = setTimeout(() => setOpen(true), DELAY);
    }, []);

    const hide = useCallback(() => {
        clearTimeout(timerRef.current);
        setOpen(false);
    }, []);

    // Measure once the bubble is in the DOM: we need its real size to center and flip it.
    useLayoutEffect(() => {
        if (!open || !anchorRef.current || !bubbleRef.current) return;
        const a = anchorRef.current.getBoundingClientRect();
        const b = bubbleRef.current.getBoundingClientRect();
        const flipped = a.top - b.height - GAP < 8;
        const left = Math.min(
            Math.max(8, a.left + a.width / 2 - b.width / 2),
            Math.max(8, window.innerWidth - b.width - 8)
        );
        setPos({ left, top: flipped ? a.bottom + GAP : a.top - b.height - GAP, flipped });
    }, [open, content]);

    // Escape closes an open tooltip, matching the modal/menu convention elsewhere.
    useLayoutEffect(() => {
        if (!open) return undefined;
        const onKey = (e) => { if (e.key === 'Escape') hide(); };
        window.addEventListener('keydown', onKey);
        window.addEventListener('scroll', hide, true);
        return () => {
            window.removeEventListener('keydown', onKey);
            window.removeEventListener('scroll', hide, true);
        };
    }, [open, hide]);

    if (content == null) return <Tag className={className} {...rest}>{children}</Tag>;

    return (
        <>
            <Tag
                ref={anchorRef}
                className={`tip-anchor ${className}`.trim()}
                tabIndex={0}
                onMouseEnter={show}
                onMouseLeave={hide}
                onFocus={show}
                onBlur={hide}
                {...rest}
            >
                {children}
            </Tag>
            {open && createPortal(
                <div
                    ref={bubbleRef}
                    className={`tip-bubble${pos.flipped ? ' is-below' : ''}`}
                    role="tooltip"
                    style={{ left: pos.left, top: pos.top }}
                >
                    {content}
                </div>,
                document.body
            )}
        </>
    );
}

/* Convenience layout for tooltip bodies: a title line, then label/value rows. */
export function TipBody({ title, note, rows = [] }) {
    return (
        <>
            {title && <p className="tip-title">{title}</p>}
            {rows.length > 0 && (
                <dl className="tip-rows">
                    {rows.map(([label, value]) => (
                        <div key={label}>
                            <dt>{label}</dt>
                            <dd>{value}</dd>
                        </div>
                    ))}
                </dl>
            )}
            {note && <p className="tip-note">{note}</p>}
        </>
    );
}
