import { useEffect, useMemo, useRef, useState } from 'react';
import FullCalendar from '@fullcalendar/react';
import timeGridPlugin from '@fullcalendar/timegrid';
import interactionPlugin, { Draggable } from '@fullcalendar/interaction';
import { getBoard, placeEntry, moveEntry, removeEntry } from './boardApi';
import { notifySuccess, notifyError } from '../shell/notify';
import { confirmAction } from '../shell/confirm';
import { REF_DATES, toIso, fromDate, fmtHours, hhmm, subjectColor, slotLabelFormat } from './calendarUtils';
import './board.css';

const DEFAULT_MINUTES = 90; // a dropped subject starts as a 90-minute block; resize to taste.

const DAY_NAMES = ['', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];

// Identifies one meeting — a subject's lecture or laboratory hours, for one faculty member and
// one class section. Pool items, tracker rows, and placed calendar entries all carry these four
// fields, so the same key links a tracker row to the blocks it is counting.
const meetingKey = (x) => `${x.facultyProfileId}|${x.subjectId}|${x.cohortKey}|${x.component}`;

export default function ScheduleBoardPage() {
    const [semesters, setSemesters] = useState([]);
    const [semesterId, setSemesterId] = useState('');
    const [semesterName, setSemesterName] = useState('');
    const [rooms, setRooms] = useState([]);
    const [roomId, setRoomId] = useState('');
    const [faculty, setFaculty] = useState([]);
    const [pool, setPool] = useState([]);
    const [entries, setEntries] = useState([]);
    const [tracker, setTracker] = useState([]);
    const [loading, setLoading] = useState(true);
    const [alert, setAlert] = useState(null);
    // Hover tooltip for a placed class: { x, y, e } where e is the entry's extendedProps.
    const [tooltip, setTooltip] = useState(null);
    const [fullscreen, setFullscreen] = useState(false);
    // Tracker row the user clicked, as its pool key ("{loadId}:{Lecture|Laboratory}"). Its blocks
    // on the calendar light up and everything else dims, so "where did these hours go?" is a
    // single click instead of a visual hunt through a full week.
    const [focusKey, setFocusKey] = useState(null);

    const pageRef = useRef(null);

    // Left-panel filters over the "Assigned Subjects" pool (and, for faculty/section, the calendar).
    const [facultyFilter, setFacultyFilter] = useState('');
    const [sectionFilter, setSectionFilter] = useState('');
    const [search, setSearch] = useState('');

    const poolRef = useRef(null);

    async function load(sid) {
        const data = await getBoard(sid || undefined);
        setSemesters(data.semesters);
        setRooms(data.rooms);
        setFaculty(data.faculty);
        setPool(data.pool);
        setEntries(data.entries);
        setTracker(data.hoursTracker);
        setSemesterName(data.semesterName || '');
        return data;
    }

    // Full (re)load when the semester changes.
    useEffect(() => {
        let active = true;
        (async () => {
            setLoading(true); setAlert(null);
            try {
                const data = await load(semesterId);
                if (!active) return;
                if (!semesterId && data.semesterId) setSemesterId(data.semesterId);
                setRoomId(prev => (
                    prev === 'all' || (prev && data.rooms.some(r => r.id === prev))
                        ? prev
                        : data.rooms[0]?.id || ''
                ));
            } catch (err) {
                if (active) setAlert(err.message);
            } finally {
                if (active) setLoading(false);
            }
        })();
        return () => { active = false; };
    }, [semesterId]);

    // Lighter refresh (pool/entries/tracker only) after a placement or removal changes the pool.
    async function refresh() {
        try {
            const data = await getBoard(semesterId || undefined);
            setPool(data.pool);
            setEntries(data.entries);
            setTracker(data.hoursTracker);
        } catch (err) {
            setAlert(err.message);
        }
    }

    // Register the pool list as a FullCalendar external drag source. Re-runs once loading
    // clears, because the pool container only mounts after the first fetch (and re-mounts
    // whenever the semester reloads) — initializing on mount alone would miss it.
    useEffect(() => {
        if (loading || !poolRef.current) return undefined;
        const dz = new Draggable(poolRef.current, {
            itemSelector: '.board-pool-item',
            eventData: (el) => ({ title: el.dataset.code, duration: { minutes: DEFAULT_MINUTES }, create: false })
        });
        return () => dz.destroy();
    }, [loading]);

    const cohorts = useMemo(() => {
        const map = new Map();
        pool.forEach(p => map.set(p.cohortKey, p.cohortLabel));
        entries.forEach(e => map.set(e.cohortKey, e.cohortLabel));
        return [...map.entries()].map(([key, label]) => ({ key, label })).sort((a, b) => a.label.localeCompare(b.label));
    }, [pool, entries]);

    const filteredPool = useMemo(() => {
        const q = search.trim().toLowerCase();
        return pool.filter(p =>
            (!facultyFilter || p.facultyProfileId === facultyFilter) &&
            (!sectionFilter || p.cohortKey === sectionFilter) &&
            (!q || p.subjectCode.toLowerCase().includes(q) || p.subjectTitle.toLowerCase().includes(q)));
    }, [pool, facultyFilter, sectionFilter, search]);

    // Weekly Hours Tracker rows: required hours come from the server; plotted hours are summed
    // live from the calendar entries so the reading updates on every drop / move / resize / remove.
    // One row per *meeting* — a lecture–laboratory subject is tracked as two, because its lecture
    // hours and laboratory hours can fall short independently.
    const trackerRows = useMemo(() => tracker
        .filter(t => (!facultyFilter || t.facultyProfileId === facultyFilter) && (!sectionFilter || t.cohortKey === sectionFilter))
        .map(t => {
            const key = meetingKey(t);
            const minutes = entries
                .filter(e => meetingKey(e) === key)
                .reduce((sum, e) => sum + (e.endMinutes - e.startMinutes), 0);
            return { ...t, meetingKey: key, plotted: minutes / 60 };
        }), [tracker, entries, facultyFilter, sectionFilter]);

    // "All rooms" is a read-across view: every placement in the semester at once,
    // each block tagged with its room. New drops still need a specific room.
    const allRooms = roomId === 'all';

    const events = useMemo(() => entries
        .filter(e => allRooms || e.roomId === roomId)
        .filter(e => !facultyFilter || e.facultyProfileId === facultyFilter)
        .filter(e => !sectionFilter || e.cohortKey === sectionFilter)
        .map(e => {
            const c = subjectColor(e.subjectId);
            const classNames = [];
            if (e.component === 'Laboratory') classNames.push('ev-lab');
            // With a tracker row focused, its own blocks lift and the rest recede.
            if (focusKey) classNames.push(meetingKey(e) === focusKey ? 'ev-focus' : 'ev-dim');
            return {
                id: e.assignmentId,
                start: toIso(e.day, e.startMinutes),
                end: toIso(e.day, e.endMinutes),
                backgroundColor: c.bg,
                borderColor: c.border,
                textColor: c.text,
                classNames,
                extendedProps: e
            };
        }), [entries, roomId, allRooms, facultyFilter, sectionFilter, focusKey]);

    // Focusing a meeting that lives in another room is useless if that room isn't on screen, so
    // clicking a tracker row also widens the calendar to "All rooms" when it needs to.
    function focusMeeting(row) {
        const key = row.meetingKey;
        if (focusKey === key) { setFocusKey(null); return; }
        setFocusKey(key);
        const placed = entries.filter(e => meetingKey(e) === key);
        if (!allRooms && placed.length > 0 && placed.some(e => e.roomId !== roomId)) {
            setRoomId('all');
        }
    }

    // Esc clears the focus, matching how the fullscreen and modal affordances behave.
    useEffect(() => {
        if (!focusKey) return undefined;
        const onKey = (ev) => { if (ev.key === 'Escape') setFocusKey(null); };
        window.addEventListener('keydown', onKey);
        return () => window.removeEventListener('keydown', onKey);
    }, [focusKey]);

    async function handleExternalDrop(info) {
        const { loadId, component } = info.draggedEl.dataset;
        if (!loadId || !roomId) return;
        if (allRooms) {
            notifyError('Pick a specific room to place a class — “All rooms” is a viewing mode.');
            return;
        }
        const { day, minutes } = fromDate(info.date);
        if (day < 1 || day > 6) return;
        setAlert(null);
        try {
            await placeEntry({
                facultyLoadAssignmentId: loadId,
                component,
                roomId,
                day,
                startMinutes: minutes,
                endMinutes: Math.min(minutes + DEFAULT_MINUTES, 18 * 60)
            });
            notifySuccess('Class placed on the board.');
            await refresh();
        } catch (err) {
            setAlert(err.message);
            notifyError(err.message);
        }
    }

    async function handleEventChange(info) {
        const e = info.event;
        const { day, minutes: startMinutes } = fromDate(e.start);
        const { minutes: endMinutes } = fromDate(e.end);
        // In the all-rooms view a moved class keeps its own room; otherwise it takes the selected one.
        const targetRoomId = allRooms ? e.extendedProps.roomId : roomId;
        try {
            const updated = await moveEntry(e.id, { roomId: targetRoomId, day, startMinutes, endMinutes });
            setEntries(prev => prev.map(x => x.assignmentId === e.id ? updated : x));
            notifySuccess('Class rescheduled.');
        } catch (err) {
            info.revert();
            setAlert(err.message);
            notifyError(err.message);
        }
    }

    async function handleEventClick(info) {
        const e = info.event.extendedProps;
        const ok = await confirmAction({
            title: 'Remove from the calendar?',
            message: `${e.subjectCode} (${e.cohortLabel}) will go back to the assigned-subjects pool.`,
            confirmLabel: 'Remove',
            danger: true
        });
        if (!ok) return;
        setAlert(null);
        try {
            await removeEntry(info.event.id);
            notifySuccess(`${e.subjectCode} (${e.cohortLabel}) returned to the pool.`);
            await refresh();
        } catch (err) {
            setAlert(err.message);
            notifyError(err.message);
        }
    }

    // Fullscreen the whole board so the whole timetable is usable on a projector or big display.
    function toggleFullscreen() {
        if (!document.fullscreenElement) {
            pageRef.current?.requestFullscreen?.();
        } else {
            document.exitFullscreen?.();
        }
    }

    // Keep local state in sync however fullscreen is left (button, Esc, or F11).
    useEffect(() => {
        const onChange = () => setFullscreen(document.fullscreenElement === pageRef.current);
        document.addEventListener('fullscreenchange', onChange);
        return () => document.removeEventListener('fullscreenchange', onChange);
    }, []);

    const selectedRoom = rooms.find(r => r.id === roomId);
    // Grow the calendar to fill the screen in fullscreen; keep the fixed height otherwise.
    const calendarHeight = fullscreen ? Math.max(520, window.innerHeight - 210) : 640;

    return (
        <div className={`board-page${fullscreen ? ' is-fullscreen' : ''}`} ref={pageRef}>
            <header className="board-head">
                <div>
                    <h2>Schedule board</h2>
                    <p className="board-sub">
                        Drag allocated subjects onto the timetable to build{' '}
                        {semesterName ? <strong>{semesterName}</strong> : 'the semester'}’s class schedule.
                        Conflicts across rooms, faculty, and sections are blocked automatically.
                    </p>
                </div>
                <div className="board-head-controls">
                    <label className="board-select">
                        <span>Semester</span>
                        <select value={semesterId} onChange={e => setSemesterId(e.target.value)} disabled={semesters.length === 0}>
                            {semesters.length === 0 && <option value="">No semesters</option>}
                            {semesters.map(s => (
                                <option key={s.id} value={s.id}>
                                    {s.name}{s.isActive ? ' (active)' : s.isArchived ? ' (archived)' : ''}
                                </option>
                            ))}
                        </select>
                    </label>
                    <label className="board-select">
                        <span>Room</span>
                        <select value={roomId} onChange={e => setRoomId(e.target.value)} disabled={rooms.length === 0}>
                            {rooms.length === 0 && <option value="">No rooms</option>}
                            {rooms.length > 0 && <option value="all">All rooms · full schedule</option>}
                            {rooms.map(r => (
                                <option key={r.id} value={r.id}>{r.name}{r.isLaboratory ? ` · ${r.kindLabel}` : ''}</option>
                            ))}
                        </select>
                    </label>
                    <button
                        type="button"
                        className="btn btn-ghost board-fs-btn"
                        onClick={toggleFullscreen}
                        aria-pressed={fullscreen}
                        title={fullscreen ? 'Exit full screen (Esc)' : 'Full screen'}
                    >
                        {fullscreen ? (
                            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
                                <path d="M9 4v3a2 2 0 0 1-2 2H4M20 9h-3a2 2 0 0 1-2-2V4M15 20v-3a2 2 0 0 1 2-2h3M4 15h3a2 2 0 0 1 2 2v3" />
                            </svg>
                        ) : (
                            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
                                <path d="M4 9V6a2 2 0 0 1 2-2h3M20 9V6a2 2 0 0 0-2-2h-3M4 15v3a2 2 0 0 0 2 2h3M20 15v3a2 2 0 0 1-2 2h-3" />
                            </svg>
                        )}
                        <span>{fullscreen ? 'Exit full screen' : 'Full screen'}</span>
                    </button>
                </div>
            </header>

            {alert && <div className="alert board-alert">{alert}</div>}

            {loading ? (
                <p className="board-empty">Loading board…</p>
            ) : (
                <div className="board-grid" style={{ '--board-cal-h': `${calendarHeight}px` }}>
                    <aside className="board-side">
                        <section className="board-panel">
                            <h3 className="board-panel-title">Filters</h3>
                            <label className="board-filter">
                                <span>Faculty</span>
                                <select value={facultyFilter} onChange={e => setFacultyFilter(e.target.value)}>
                                    <option value="">All faculty</option>
                                    {faculty.map(f => <option key={f.id} value={f.id}>{f.name}</option>)}
                                </select>
                            </label>
                            <label className="board-filter">
                                <span>Section</span>
                                <select value={sectionFilter} onChange={e => setSectionFilter(e.target.value)}>
                                    <option value="">All sections</option>
                                    {cohorts.map(c => <option key={c.key} value={c.key}>{c.label}</option>)}
                                </select>
                            </label>
                            <input
                                className="board-search"
                                type="search"
                                placeholder="Search subject…"
                                value={search}
                                onChange={e => setSearch(e.target.value)}
                            />
                        </section>

                        <section className="board-panel">
                            <h3 className="board-panel-title">
                                Assigned subjects
                                <span className="board-count">{filteredPool.length}</span>
                            </h3>
                            <p className="board-panel-hint">
                                {allRooms
                                    ? 'Viewing every room’s schedule — pick a specific room to place new classes.'
                                    : `Drag a meeting onto the calendar for ${selectedRoom?.name || 'a room'}` +
                                      `${selectedRoom ? ` (${selectedRoom.kindLabel.toLowerCase()})` : ''}. ` +
                                      'Lecture and laboratory hours are dragged separately.'}
                            </p>
                            <div className="board-pool" ref={poolRef}>
                                {filteredPool.length === 0 ? (
                                    <p className="board-pool-empty">
                                        {pool.length === 0 ? 'Every allocated subject is scheduled.' : 'No subjects match these filters.'}
                                    </p>
                                ) : filteredPool.map(p => (
                                    <div
                                        key={p.key}
                                        className={[
                                            'board-pool-item',
                                            p.requiresLaboratory ? 'is-lab' : '',
                                            focusKey && meetingKey(p) === focusKey ? 'is-focus' : '',
                                            focusKey && meetingKey(p) !== focusKey ? 'is-dim' : ''
                                        ].filter(Boolean).join(' ')}
                                        data-load-id={p.facultyLoadAssignmentId}
                                        data-component={p.component}
                                        data-code={p.subjectCode}
                                        title={`${p.subjectCode} — ${p.subjectTitle} · ${p.remainingHours}h of ${p.componentLabel.toLowerCase()} left, needs a ${p.requiredRoomKindLabel.toLowerCase()}`}
                                        style={{ borderLeftColor: subjectColor(p.subjectId).border }}
                                    >
                                        <div className="board-pool-main">
                                            <span className="board-pool-code">{p.subjectCode}</span>
                                            <span className="board-pool-units">{p.units}u</span>
                                            <span className={`chip ${p.requiresLaboratory ? 'chip-lab' : 'chip-muted'}`}>
                                                {p.component === 'Laboratory' ? 'Lab' : 'Lec'}
                                            </span>
                                        </div>
                                        <div className="board-pool-title">{p.subjectTitle}</div>
                                        <div className="board-pool-meta">
                                            <span>{p.cohortLabel}</span>
                                            <span aria-hidden>·</span>
                                            <span>{p.facultyName}</span>
                                        </div>
                                        <div className="board-pool-need">
                                            {fmtHours(p.remainingHours)}h left · {p.requiredRoomKindLabel}
                                        </div>
                                    </div>
                                ))}
                            </div>
                        </section>
                    </aside>

                    <main className="board-cal card">
                        {rooms.length === 0 ? (
                            <p className="board-empty">Add rooms in Academic setup to start scheduling.</p>
                        ) : (
                            <FullCalendar
                                plugins={[timeGridPlugin, interactionPlugin]}
                                initialView="timeGridWeek"
                                initialDate={REF_DATES[0]}
                                headerToolbar={false}
                                hiddenDays={[0]}
                                allDaySlot={false}
                                dayHeaderFormat={{ weekday: 'long' }}
                                slotMinTime="07:00:00"
                                slotMaxTime="18:00:00"
                                slotDuration="00:30:00"
                                snapDuration="00:30:00"
                                slotLabelFormat={slotLabelFormat()}
                                expandRows
                                height={calendarHeight}
                                nowIndicator={false}
                                editable
                                droppable
                                eventDurationEditable
                                drop={handleExternalDrop}
                                eventDrop={handleEventChange}
                                eventResize={handleEventChange}
                                eventClick={handleEventClick}
                                eventMouseEnter={(info) => setTooltip({
                                    x: info.jsEvent.clientX,
                                    y: info.jsEvent.clientY,
                                    e: info.event.extendedProps
                                })}
                                eventMouseLeave={() => setTooltip(null)}
                                events={events}
                                eventContent={(arg) => {
                                    const e = arg.event.extendedProps;
                                    return (
                                        <div className="ev">
                                            <span className="ev-code">{e.subjectCode}</span>
                                            {allRooms && <span className="ev-meta ev-room">{e.roomName}</span>}
                                            <span className="ev-meta">{e.cohortLabel}</span>
                                            <span className="ev-meta">{e.facultyName}</span>
                                        </div>
                                    );
                                }}
                            />
                        )}
                    </main>

                    <aside className="board-track-side">
                        <section className="board-panel board-track-panel">
                            <h3 className="board-panel-title">
                                Weekly hours tracker
                                <span className="board-count">{trackerRows.length}</span>
                            </h3>
                            <p className="board-panel-hint">
                                {focusKey
                                    ? 'Highlighting this meeting’s blocks — click it again or press Esc to clear.'
                                    : 'Plotted hours vs. each meeting’s weekly requirement. Click a row to highlight its blocks on the calendar.'}
                            </p>
                            <div className="board-tracker">
                                {trackerRows.length === 0 ? (
                                    <p className="board-pool-empty">No assigned subjects for these filters.</p>
                                ) : trackerRows.map(t => {
                                    const pct = t.requiredHours > 0 ? Math.min(100, Math.round((t.plotted / t.requiredHours) * 100)) : 0;
                                    const over = t.plotted > t.requiredHours;
                                    const done = !over && t.requiredHours > 0 && t.plotted >= t.requiredHours;
                                    const status = over ? 'is-over' : done ? 'is-done' : 'is-todo';
                                    const focused = focusKey === t.meetingKey;
                                    return (
                                        <button
                                            type="button"
                                            key={t.key}
                                            className={[
                                                'board-track-row',
                                                focused ? 'is-focus' : '',
                                                focusKey && !focused ? 'is-dim' : ''
                                            ].filter(Boolean).join(' ')}
                                            aria-pressed={focused}
                                            onClick={() => focusMeeting(t)}
                                        >
                                            <div className="board-track-top">
                                                <span className="board-track-name">
                                                    <span className="board-track-dot" style={{ background: subjectColor(t.subjectId).border }} />
                                                    {t.subjectCode}
                                                    <span className={`chip ${t.component === 'Laboratory' ? 'chip-lab' : 'chip-muted'}`}>
                                                        {t.component === 'Laboratory' ? 'Lab' : 'Lec'}
                                                    </span>
                                                </span>
                                                <span className={`board-track-val ${status}`}>{fmtHours(t.plotted)}/{t.requiredHours}h</span>
                                            </div>
                                            <div className="board-track-meta">{t.cohortLabel} · {t.facultyName}</div>
                                            <div className="board-track-bar">
                                                <span className={`board-track-fill ${status}`} style={{ width: `${pct}%` }} />
                                            </div>
                                            {done && <span className="board-track-note is-done-note">Fully plotted ✓</span>}
                                            {over && <span className="board-track-note is-over-note">{fmtHours(t.plotted - t.requiredHours)}h over the requirement</span>}
                                            {!done && !over && (
                                                <span className="board-track-note">
                                                    {t.plotted === 0
                                                        ? `Needs ${t.requiredHours}h in a ${t.requiredRoomKindLabel.toLowerCase()}`
                                                        : `${fmtHours(t.requiredHours - t.plotted)}h left to plot`}
                                                </span>
                                            )}
                                        </button>
                                    );
                                })}
                            </div>
                        </section>
                    </aside>
                </div>
            )}

            {tooltip && (() => {
                const { x, y, e } = tooltip;
                // Keep the tooltip on-screen: flip it left/up when near the right/bottom edge.
                const flipX = x > window.innerWidth - 280;
                const flipY = y > window.innerHeight - 220;
                const style = {
                    left: x + (flipX ? -14 : 14),
                    top: y + (flipY ? -14 : 14),
                    transform: `translate(${flipX ? '-100%' : '0'}, ${flipY ? '-100%' : '0'})`
                };
                const durationH = (e.endMinutes - e.startMinutes) / 60;
                return (
                    <div className="board-tooltip" role="tooltip" style={style}>
                        <div className="board-tooltip-head">
                            <span className="board-tooltip-dot" style={{ background: subjectColor(e.subjectId).border }} />
                            <span className="board-tooltip-code">{e.subjectCode}</span>
                            <span className={`chip ${e.component === 'Laboratory' ? 'chip-lab' : 'chip-muted'}`}>
                                {e.component === 'Laboratory' ? 'Lab' : 'Lec'}
                            </span>
                            <span className={`board-tooltip-tag ${e.isPublished ? 'is-published' : 'is-draft'}`}>
                                {e.isPublished ? 'Published' : 'Draft'}
                            </span>
                        </div>
                        <div className="board-tooltip-title">{e.subjectTitle}</div>
                        <dl className="board-tooltip-grid">
                            <div><dt>When</dt><dd>{DAY_NAMES[e.day]} · {hhmm(e.startMinutes)}–{hhmm(e.endMinutes)} ({fmtHours(durationH)}h)</dd></div>
                            <div><dt>Room</dt><dd>{e.roomName}</dd></div>
                            <div><dt>Meeting</dt><dd>{e.component} · {e.deliveryShort}</dd></div>
                            <div><dt>Section</dt><dd>{e.cohortLabel}</dd></div>
                            <div><dt>Faculty</dt><dd>{e.facultyName}</dd></div>
                        </dl>
                        {e.isManualOverride && <div className="board-tooltip-note">Manually overridden</div>}
                    </div>
                );
            })()}
        </div>
    );
}
