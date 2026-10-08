import { createPortal } from 'react-dom';
import { DAY_NAMES, hhmm, fmtHours, subjectColor } from './calendarUtils';

/* The hover card for a class block, shared by the Schedule board and the read-only My schedule.

   It existed only on the board, which meant the view most people actually use — their own weekly
   timetable — gave no detail at all on hover. Rather than copy seventy lines into the second page
   (the kind of duplication this codebase already has too much of, see the day/time helpers), the
   card lives here and both pages render it.

   The two callers carry *different* entry shapes: the board knows about components, delivery,
   overrides and amendments; My schedule knows about seat counts and nothing about drafts. So every
   row below is conditional on the field being present, and the card shows what the caller happens
   to know rather than demanding one merged shape from both endpoints. */

export default function ScheduleTooltip({ x, y, entry }) {
    if (!entry) return null;

    // Keep the card on-screen: flip it left/up when the cursor is near the right/bottom edge.
    const flipX = x > window.innerWidth - 280;
    const flipY = y > window.innerHeight - 220;
    const style = {
        left: x + (flipX ? -14 : 14),
        top: y + (flipY ? -14 : 14),
        transform: `translate(${flipX ? '-100%' : '0'}, ${flipY ? '-100%' : '0'})`
    };

    const durationH = (entry.endMinutes - entry.startMinutes) / 60;
    // The two endpoints name this field differently; normalise here rather than changing a DTO
    // that other callers already read.
    const room = entry.roomName ?? entry.room;

    /* Positioned `fixed` against the cursor's viewport coordinates. The board page's rise animation
       makes it a containing block, so a card nested inside it would be measured from the page
       corner rather than the viewport — the misplacement this portal fixes. But a fullscreen element
       only paints its own subtree in the top layer, so while the board is full-screen the card must
       stay inside it — which is exactly what document.fullscreenElement is. */
    const portalTarget = document.fullscreenElement || document.body;

    return createPortal((
        <div className="board-tooltip" role="tooltip" style={style}>
            <div className="board-tooltip-head">
                <span className="board-tooltip-dot" style={{ background: subjectColor(entry.subjectId).border }} />
                <span className="board-tooltip-code">{entry.subjectCode}</span>
                {entry.component && (
                    <span className={`chip ${entry.component === 'Laboratory' ? 'chip-lab' : 'chip-muted'}`}>
                        {entry.component === 'Laboratory' ? 'Lab' : 'Lec'}
                    </span>
                )}
                {/* Draft vs published is a board concern — on My schedule everything shown is
                    already published, so saying so would be noise. */}
                {entry.isPublished === false && (
                    <span className="board-tooltip-tag is-draft">Draft</span>
                )}
                {entry.isAmended && <span className="board-tooltip-tag is-amended">Amended</span>}
            </div>

            <div className="board-tooltip-title">{entry.subjectTitle}</div>

            <dl className="board-tooltip-grid">
                <div>
                    <dt>When</dt>
                    <dd>
                        {DAY_NAMES[entry.day]} · {hhmm(entry.startMinutes)}–{hhmm(entry.endMinutes)}
                        {' '}({fmtHours(durationH)}h)
                    </dd>
                </div>
                {room && <div><dt>Room</dt><dd>{room}</dd></div>}
                {entry.component && (
                    <div>
                        <dt>Meeting</dt>
                        <dd>{entry.component}{entry.deliveryShort ? ` · ${entry.deliveryShort}` : ''}</dd>
                    </div>
                )}
                {entry.cohortLabel && <div><dt>Section</dt><dd>{entry.cohortLabel}</dd></div>}
                {entry.facultyName && <div><dt>Faculty</dt><dd>{entry.facultyName}</dd></div>}
                {/* Seat counts only exist on My schedule, and only mean something there. */}
                {entry.capacity > 0 && (
                    <div><dt>Seats</dt><dd>{entry.enrolled} / {entry.capacity}</dd></div>
                )}
            </dl>

            {entry.isAmended && (
                <div className="board-tooltip-note">
                    Changed after publication — the faculty member and enrolled students were notified.
                </div>
            )}
            {entry.isManualOverride && !entry.isAmended && (
                <div className="board-tooltip-note">Manually overridden</div>
            )}
        </div>
    ), portalTarget);
}
