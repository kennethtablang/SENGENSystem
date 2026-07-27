import { useEffect, useMemo } from 'react';
import { createPortal } from 'react-dom';
import { hhmm } from './calendarUtils';

/* Room selection for a drop made in the "All rooms" view (FR-SCHED-02). The board can show every
   room's schedule at once, but a placement has to land in one specific room — so instead of
   refusing the drop, this asks which one, right at the moment the decision has to be made.

   Every room is listed, so the choice is made against the full picture; the ones that cannot take
   this class are muted with the reason why:
     · the room is the wrong kind for the meeting (H3b — laboratory hours need the laboratory the
       subject requires, lecture hours a lecture room), or
     · it is already booked over the dropped time window.
   The server re-checks both — this only spares the officer a refused drop. */

const DAY_NAMES = ['', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];

/** Overlap on the same day: start < otherEnd && otherStart < end. */
const overlaps = (entry, day, start, end) =>
    entry.day === day && start < entry.endMinutes && entry.startMinutes < end;

/**
 * Why a room can't take this meeting, or null when it can. Room kind is checked first: an
 * unavailable lecture room is still a lecture room, but a laboratory subject would never be
 * placed in one however free it is.
 */
function unavailableReason(room, meeting, entries, day, start, end) {
    if (room.kind !== meeting.requiredRoomKind) {
        return `${room.kindLabel} — this meeting needs a ${meeting.requiredRoomKindLabel.toLowerCase()}`;
    }
    const clash = entries.find(e => e.roomId === room.id && overlaps(e, day, start, end));
    if (clash) {
        return `Booked ${hhmm(clash.startMinutes)}–${hhmm(clash.endMinutes)} · ${clash.subjectCode} (${clash.cohortLabel})`;
    }
    return null;
}

export default function RoomPickerModal({ request, rooms, entries, busy, onPick, onCancel }) {
    const { day, startMinutes, endMinutes, meeting } = request;

    useEffect(() => {
        const onKey = (e) => { if (e.key === 'Escape' && !busy) onCancel(); };
        window.addEventListener('keydown', onKey);
        return () => window.removeEventListener('keydown', onKey);
    }, [busy, onCancel]);

    // Rooms that can take the class first, each keeping its own reason when it can't. Within a
    // group the smallest room leads: the snuggest fit is the one worth defaulting to.
    const choices = useMemo(() => rooms
        .map(room => ({
            room,
            reason: unavailableReason(room, meeting, entries, day, startMinutes, endMinutes)
        }))
        .sort((a, b) =>
            (a.reason ? 1 : 0) - (b.reason ? 1 : 0)
            || a.room.capacity - b.room.capacity
            || a.room.name.localeCompare(b.room.name)),
        [rooms, entries, meeting, day, startMinutes, endMinutes]);

    const availableCount = choices.filter(c => !c.reason).length;

    return createPortal(
        <div className="modal-overlay" onClick={() => !busy && onCancel()} role="presentation">
            <div
                className="modal room-picker"
                role="dialog"
                aria-modal="true"
                aria-label="Choose a room"
                onClick={e => e.stopPropagation()}
            >
                <header className="modal-head">
                    <h2>Choose a room</h2>
                    <button type="button" className="modal-close" onClick={onCancel} aria-label="Close" disabled={busy}>×</button>
                </header>

                <div className="modal-body">
                    <p className="room-picker-lead">
                        <strong>{meeting.subjectCode}</strong> {meeting.component === 'Laboratory' ? 'laboratory' : 'lecture'} hours
                        for {meeting.cohortLabel} with {meeting.facultyName},{' '}
                        {DAY_NAMES[day]} {hhmm(startMinutes)}–{hhmm(endMinutes)}.
                    </p>
                    <p className="room-picker-count">
                        {availableCount === 0
                            ? `No room is free for this slot — every ${meeting.requiredRoomKindLabel.toLowerCase()} is either taken or the wrong kind.`
                            : `${availableCount} of ${choices.length} rooms can take it. The rest are shown with why they can’t.`}
                    </p>

                    <ul className="room-picker-list">
                        {choices.map(({ room, reason }) => (
                            <li key={room.id}>
                                <button
                                    type="button"
                                    className={`room-picker-item${reason ? ' is-unavailable' : ''}`}
                                    disabled={!!reason || busy}
                                    title={reason ?? `Place in ${room.name}`}
                                    onClick={() => onPick(room.id)}
                                >
                                    <span className="room-picker-main">
                                        <span className="room-picker-name">{room.name}</span>
                                        <span className={`chip ${room.isLaboratory ? 'chip-lab' : 'chip-muted'}`}>
                                            {room.kindLabel}
                                        </span>
                                    </span>
                                    <span className="room-picker-meta">
                                        {room.capacity} seats
                                    </span>
                                    <span className={`room-picker-state${reason ? '' : ' is-free'}`}>
                                        {reason ?? 'Available'}
                                    </span>
                                </button>
                            </li>
                        ))}
                    </ul>
                </div>

                <footer className="modal-foot">
                    <span className="setup-foot-spacer" />
                    <button type="button" className="btn btn-ghost" onClick={onCancel} disabled={busy}>Cancel</button>
                </footer>
            </div>
        </div>,
        // A fullscreen element only paints its own subtree, so while the board is full-screen the
        // overlay has to live inside it — which is exactly document.fullscreenElement.
        document.fullscreenElement || document.body
    );
}
