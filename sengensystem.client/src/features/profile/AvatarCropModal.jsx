import { useEffect, useRef, useState } from 'react';
import SetupModal from '../academic/SetupModal';
import { uploadAvatar } from './api';
import { primeAvatar } from '../shell/avatarCache';
import { notifySuccess, notifyError } from '../shell/notify';

/* Position and zoom a picture inside the circle, then upload it.

   The crop happens here, in the browser: the chosen square is redrawn onto a 512×512 canvas and
   saved as a JPEG. That keeps uploads to a few dozen KB whatever the camera produced, and — because
   the picture is re-encoded rather than passed through — drops its EXIF metadata, including the
   GPS location a phone writes into every photo. */

const VIEW = 280;     // preview size in CSS pixels
const OUTPUT = 512;   // uploaded size
const MAX_ZOOM = 3;

export default function AvatarCropModal({ file, onClose, onSaved }) {
    const canvasRef = useRef(null);
    const [image, setImage] = useState(null);
    const [error, setError] = useState('');
    const [zoom, setZoom] = useState(1);
    const [offset, setOffset] = useState({ x: 0, y: 0 });
    const [busy, setBusy] = useState(false);
    const drag = useRef(null);

    // Decode the file. Browsers that cannot read a format (HEIC on most desktops) fail here.
    useEffect(() => {
        const url = URL.createObjectURL(file);
        const img = new Image();
        img.onload = () => setImage(img);
        img.onerror = () => setError('This picture could not be opened. Use a JPEG, PNG, or WebP image.');
        img.src = url;
        return () => URL.revokeObjectURL(url);
    }, [file]);

    // The scale that makes the picture just cover the square, and the furthest it may be moved
    // before an edge shows inside the circle.
    const base = image ? Math.max(VIEW / image.naturalWidth, VIEW / image.naturalHeight) : 1;
    const scale = base * zoom;
    const limit = image
        ? { x: Math.max(0, (image.naturalWidth * scale - VIEW) / 2), y: Math.max(0, (image.naturalHeight * scale - VIEW) / 2) }
        : { x: 0, y: 0 };
    const clamp = o => ({
        x: Math.min(limit.x, Math.max(-limit.x, o.x)),
        y: Math.min(limit.y, Math.max(-limit.y, o.y))
    });
    const shown = clamp(offset);

    function draw(ctx, size) {
        const k = size / VIEW;
        const w = image.naturalWidth * scale * k;
        const h = image.naturalHeight * scale * k;
        ctx.fillStyle = '#ffffff'; // JPEG has no transparency: a transparent PNG gets a white ground
        ctx.fillRect(0, 0, size, size);
        ctx.imageSmoothingQuality = 'high';
        ctx.drawImage(image, (size - w) / 2 + shown.x * k, (size - h) / 2 + shown.y * k, w, h);
    }

    useEffect(() => {
        const canvas = canvasRef.current;
        if (!canvas || !image) return;
        const ratio = window.devicePixelRatio || 1;
        canvas.width = VIEW * ratio;
        canvas.height = VIEW * ratio;
        draw(canvas.getContext('2d'), VIEW * ratio);
    });

    function onPointerDown(e) {
        e.currentTarget.setPointerCapture(e.pointerId);
        drag.current = { x: e.clientX, y: e.clientY, start: shown };
    }
    function onPointerMove(e) {
        if (!drag.current) return;
        const d = drag.current;
        setOffset(clamp({ x: d.start.x + e.clientX - d.x, y: d.start.y + e.clientY - d.y }));
    }
    function onPointerUp() { drag.current = null; }

    function onKeyDown(e) {
        const step = e.shiftKey ? 20 : 5;
        const moves = { ArrowLeft: [step, 0], ArrowRight: [-step, 0], ArrowUp: [0, step], ArrowDown: [0, -step] };
        if (moves[e.key]) {
            e.preventDefault();
            setOffset(clamp({ x: shown.x + moves[e.key][0], y: shown.y + moves[e.key][1] }));
        } else if (e.key === '+' || e.key === '=') {
            setZoom(z => Math.min(MAX_ZOOM, +(z + 0.1).toFixed(2)));
        } else if (e.key === '-') {
            setZoom(z => Math.max(1, +(z - 0.1).toFixed(2)));
        }
    }

    function onWheel(e) {
        setZoom(z => Math.min(MAX_ZOOM, Math.max(1, +(z - Math.sign(e.deltaY) * 0.1).toFixed(2))));
    }

    async function save() {
        if (!image) return;
        setBusy(true);
        setError('');
        try {
            const out = document.createElement('canvas');
            out.width = OUTPUT;
            out.height = OUTPUT;
            draw(out.getContext('2d'), OUTPUT);
            const blob = await new Promise((resolve, reject) =>
                out.toBlob(b => (b ? resolve(b) : reject(new Error('The picture could not be prepared.'))), 'image/jpeg', 0.9));
            const { user } = await uploadAvatar(blob);
            primeAvatar(user, blob);
            notifySuccess('Profile picture updated.');
            onSaved(user);
        } catch (err) {
            setError(err.message);
            notifyError(err.message);
            setBusy(false);
        }
    }

    const footer = (
        <>
            <button type="button" className="btn btn-ghost" onClick={onClose} disabled={busy}>Cancel</button>
            <button type="button" className="btn btn-primary" onClick={save} disabled={!image || busy}>
                {busy ? 'Saving…' : 'Save picture'}
            </button>
        </>
    );

    return (
        <SetupModal title="Profile picture" onClose={busy ? () => {} : onClose} footer={footer} className="avatar-crop-modal">
            {error && <div className="alert" role="alert">{error}</div>}
            {!error && (
                <div className="avatar-crop">
                    <p className="avatar-crop-hint">Drag to position, and zoom to fit your face inside the circle.</p>
                    <div className="avatar-crop-stage" style={{ width: VIEW, height: VIEW }}>
                        <canvas
                            ref={canvasRef}
                            style={{ width: VIEW, height: VIEW }}
                            tabIndex={0}
                            role="img"
                            aria-label="Picture preview. Use the arrow keys to move it and plus or minus to zoom."
                            onPointerDown={onPointerDown}
                            onPointerMove={onPointerMove}
                            onPointerUp={onPointerUp}
                            onPointerCancel={onPointerUp}
                            onKeyDown={onKeyDown}
                            onWheel={onWheel}
                        />
                        <span className="avatar-crop-ring" aria-hidden="true" />
                        {!image && <span className="avatar-crop-loading">Loading…</span>}
                    </div>
                    <label className="avatar-crop-zoom">
                        <span>Zoom</span>
                        <input
                            type="range" min="1" max={MAX_ZOOM} step="0.01" value={zoom}
                            disabled={!image}
                            onChange={e => setZoom(Number(e.target.value))}
                        />
                    </label>
                </div>
            )}
        </SetupModal>
    );
}
