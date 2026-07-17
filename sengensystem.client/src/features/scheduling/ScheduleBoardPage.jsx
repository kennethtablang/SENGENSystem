import { useEffect, useMemo, useRef, useState } from 'react';
import FullCalendar from '@fullcalendar/react';
import timeGridPlugin from '@fullcalendar/timegrid';
import interactionPlugin, { Draggable } from '@fullcalendar/interaction';
import { getBoard, placeEntry, moveEntry, removeEntry } from './boardApi';
import { notifySuccess, notifyError } from '../shell/notify';
import { confirmAction } from '../shell/confirm';
import { REF_DATES, toIso, fromDate, fmtHours, subjectColor, slotLabelFormat } from './calendarUtils';
import './board.css';

const DEFAULT_MINUTES = 90; // a dropped subject starts as a 90-minute block; resize to taste.

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
    const trackerRows = useMemo(() => tracker
        .filter(t => (!facultyFilter || t.facultyProfileId === facultyFilter) && (!sectionFilter || t.cohortKey === sectionFilter))
        .map(t => {
            const minutes = entries
                .filter(e => e.facultyProfileId === t.facultyProfileId && e.subjectId === t.subjectId && e.cohortKey === t.cohortKey)
                .reduce((sum, e) => sum + (e.endMinutes - e.startMinutes), 0);
            return { ...t, plotted: minutes / 60 };
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
            return {
                id: e.assignmentId,
                start: toIso(e.day, e.startMinutes),
                end: toIso(e.day, e.endMinutes),
                backgroundColor: c.bg,
                borderColor: c.border,
                textColor: c.text,
                classNames: e.requiresLaboratory ? ['ev-lab'] : [],
                extendedProps: e
            };
        }), [entries, roomId, allRooms, facultyFilter, sectionFilter]);

    async function handleExternalDrop(info) {
        const loadId = info.draggedEl.dataset.loadId;
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

    const selectedRoom = rooms.find(r => r.id === roomId);

    return (
        <div className="board-page">
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
                                <option key={r.id} value={r.id}>{r.name}{r.isLaboratory ? ' · Lab' : ''}</option>
                            ))}
                        </select>
                    </label>
                </div>
            </header>

            {alert && <div className="alert board-alert">{alert}</div>}

            {loading ? (
                <p className="board-empty">Loading board…</p>
            ) : (
                <div className="board-grid">
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
                                    : `Drag a subject onto the calendar for ${selectedRoom?.name || 'a room'}.`}
                            </p>
                            <div className="board-pool" ref={poolRef}>
                                {filteredPool.length === 0 ? (
                                    <p className="board-pool-empty">
                                        {pool.length === 0 ? 'Every allocated subject is scheduled.' : 'No subjects match these filters.'}
                                    </p>
                                ) : filteredPool.map(p => (
                                    <div
                                        key={p.facultyLoadAssignmentId}
                                        className={`board-pool-item${p.requiresLaboratory ? ' is-lab' : ''}`}
                                        data-load-id={p.facultyLoadAssignmentId}
                                        data-code={p.subjectCode}
                                        title={`${p.subjectCode} — ${p.subjectTitle}`}
                                        style={{ borderLeftColor: subjectColor(p.subjectId).border }}
                                    >
                                        <div className="board-pool-main">
                                            <span className="board-pool-code">{p.subjectCode}</span>
                                            <span className="board-pool-units">{p.units}u</span>
                                            {p.requiresLaboratory && <span className="chip chip-lab">Lab</span>}
                                        </div>
                                        <div className="board-pool-title">{p.subjectTitle}</div>
                                        <div className="board-pool-meta">
                                            <span>{p.cohortLabel}</span>
                                            <span aria-hidden>·</span>
                                            <span>{p.facultyName}</span>
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
                                height={640}
                                nowIndicator={false}
                                editable
                                droppable
                                eventDurationEditable
                                drop={handleExternalDrop}
                                eventDrop={handleEventChange}
                                eventResize={handleEventChange}
                                eventClick={handleEventClick}
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
                        <section className="board-panel">
                            <h3 className="board-panel-title">Weekly hours tracker</h3>
                            <p className="board-panel-hint">Plotted hours vs. each subject’s weekly requirement.</p>
                            <div className="board-tracker">
                                {trackerRows.length === 0 ? (
                                    <p className="board-pool-empty">No assigned subjects for these filters.</p>
                                ) : trackerRows.map(t => {
                                    const pct = t.requiredHours > 0 ? Math.min(100, Math.round((t.plotted / t.requiredHours) * 100)) : 0;
                                    const over = t.plotted > t.requiredHours;
                                    const done = !over && t.requiredHours > 0 && t.plotted >= t.requiredHours;
                                    const status = over ? 'is-over' : done ? 'is-done' : 'is-todo';
                                    return (
                                        <div key={t.facultyLoadAssignmentId} className="board-track-row">
                                            <div className="board-track-top">
                                                <span className="board-track-name">
                                                    <span className="board-track-dot" style={{ background: subjectColor(t.subjectId).border }} />
                                                    {t.subjectCode}
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
                                                    {t.plotted === 0 ? 'Needs plotting on the calendar' : `${fmtHours(t.requiredHours - t.plotted)}h left to plot`}
                                                </span>
                                            )}
                                        </div>
                                    );
                                })}
                            </div>
                        </section>
                    </aside>
                </div>
            )}
        </div>
    );
}
