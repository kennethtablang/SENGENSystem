import { useEffect, useMemo, useRef, useState } from 'react';
import { getRoomUtilization, downloadRoomUtilizationWorkbook, downloadRoomGridSchedule } from './api';
import { subscribeToReports } from '../reports/live';
import { LiveChip } from '../reports/ReportsPage';
import { notifySuccess, notifyError } from '../shell/notify';
import '../registration/registration.css';
import './analytics.css';

/* Room Utilization Analysis (FR-DASH-02): classroom usage across the institution for a
   chosen semester. Rooms are scored against the schedulable week and banded, so the
   under-used space stands out instead of being averaged away. */

// Bands mirror Classify() on the server. Hints quote both the percentage and the hours it
// works out to against the 45 h schedulable week, so the threshold is legible either way.
const levels = [
    { key: 'Critical', label: 'Critical', hint: 'Under 15% — less than 6.75 h of the 45 h week.' },
    { key: 'Low', label: 'Low usage', hint: '15–30% — roughly 6.75 to 13.5 h of the 45 h week.' },
    { key: 'Moderate', label: 'Moderate', hint: '30–60% — roughly 13.5 to 27 h of the 45 h week.' },
    { key: 'Optimal', label: 'Well utilized', hint: '60% and above — 27 h or more of the 45 h week.' }
];

const levelLabel = key => levels.find(l => l.key === key)?.label ?? key;

function Tile({ label, value, suffix, tone, hint }) {
    return (
        <div className={`rua-tile${tone ? ` tone-${tone}` : ''}`} title={hint}>
            <span className="rua-tile-label">{label}</span>
            <strong className="rua-tile-value">
                {value}{suffix && <small>{suffix}</small>}
            </strong>
        </div>
    );
}

/* States the methodology on the page itself: administrators reading a percentage need to
   know what it is a percentage *of*, or "42%" is not actionable. */
function WindowNote({ window: w }) {
    if (!w) return null;
    return (
        <aside className="rua-basis">
            <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor"
                strokeWidth="1.9" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
                <path d="M12 22a10 10 0 1 0 0-20 10 10 0 0 0 0 20M12 16v-4M12 8h.01" />
            </svg>
            <p>
                <strong>How utilization is measured.</strong>{' '}
                Rooms are scored against the schedulable week — {w.days}, {w.startTime}–{w.endTime},
                which is <strong>{w.hoursPerDay} hours a day × {w.daysPerWeek} days
                = {w.hoursPerWeek} hours a week</strong>. A room holding {w.hoursPerWeek / 2} hours of
                classes therefore reads 50%. Teaching outside that window — Saturdays, or before{' '}
                {w.startTime} and after {w.endTime} — is shown in a room’s total hours but does not
                count toward its utilization rate.
            </p>
        </aside>
    );
}

function RoomCard({ room }) {
    const tone = room.level.toLowerCase();
    return (
        <article className={`rua-card tone-${tone}`}>
            <header className="rua-card-head">
                <div>
                    <h3>{room.room}</h3>
                    <p className="rua-card-where">
                        {room.building}
                        {room.buildingCode && <span className="rua-code">{room.buildingCode}</span>}
                    </p>
                </div>
                <span className={`chip rua-type${room.isLaboratory ? ' lab' : ''}`}>{room.type}</span>
            </header>

            <dl className="rua-facts">
                <div>
                    <dt>Capacity</dt>
                    <dd>{room.capacity} seats</dd>
                </div>
                <div>
                    <dt>Classes</dt>
                    <dd>{room.classes} scheduled</dd>
                </div>
                <div>
                    <dt>Hours</dt>
                    <dd title={`${room.windowHoursPerWeek} h inside Mon–Fri 08:00–17:00 of ${room.hoursPerWeek} h booked in total`}>
                        {room.windowHoursPerWeek} / {room.schedulableHours} per week
                    </dd>
                </div>
            </dl>

            <div className="rua-meter">
                <div className="rua-meter-top">
                    <span className="rua-pct">{room.utilizationPct}%</span>
                    <span className="rua-meter-cap">of {room.schedulableHours} h/week</span>
                </div>
                <div
                    className="rua-bar"
                    role="progressbar"
                    aria-valuenow={room.utilizationPct}
                    aria-valuemin={0}
                    aria-valuemax={100}
                    aria-label={`${room.room} utilization`}
                >
                    <span style={{ width: `${Math.min(100, room.utilizationPct)}%` }} />
                </div>
            </div>

            <footer className="rua-status">
                {room.level === 'Critical' && (
                    <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor"
                        strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
                        <path d="M10.3 3.9 1.8 18a2 2 0 0 0 1.7 3h17a2 2 0 0 0 1.7-3L13.7 3.9a2 2 0 0 0-3.4 0M12 9v4M12 17h.01" />
                    </svg>
                )}
                {room.status}
                {room.hoursPerWeek > room.windowHoursPerWeek && (
                    <span className="rua-outside">
                        +{Math.round((room.hoursPerWeek - room.windowHoursPerWeek) * 10) / 10} h outside window
                    </span>
                )}
            </footer>
        </article>
    );
}

function RoomUtilizationPage() {
    const [semesters, setSemesters] = useState([]);
    const [semesterId, setSemesterId] = useState('');
    const [data, setData] = useState(null);
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState(null);

    const [search, setSearch] = useState('');
    const [level, setLevel] = useState('all');
    const [building, setBuilding] = useState('all');

    const [liveState, setLiveState] = useState('offline');
    const [updatedAt, setUpdatedAt] = useState(null);
    const [refreshTick, setRefreshTick] = useState(0);
    const [exporting, setExporting] = useState(null); // which download is running
    const debounceRef = useRef(null);

    // Exports always cover every room for the semester, not the filtered view — filters are
    // a reading aid here, whereas the export is the institutional record.
    async function runExport(key, fn, message) {
        setExporting(key);
        try {
            await fn(semesterId);
            notifySuccess(message);
        } catch (err) {
            notifyError(err.message);
        } finally {
            setExporting(null);
        }
    }

    // Any schedule change moves these numbers — ride the existing reports push channel.
    useEffect(() => {
        const unsubscribe = subscribeToReports(
            () => {
                clearTimeout(debounceRef.current);
                debounceRef.current = setTimeout(() => setRefreshTick(t => t + 1), 400);
            },
            state => setLiveState(state));
        return () => {
            clearTimeout(debounceRef.current);
            unsubscribe();
        };
    }, []);

    useEffect(() => {
        let live = true;
        (async () => {
            setError(null);
            try {
                const payload = await getRoomUtilization(semesterId || undefined);
                if (!live) return;
                setData(payload);
                setSemesters(payload.semesters ?? []);
                if (!semesterId && payload.semesterId) setSemesterId(payload.semesterId);
                setUpdatedAt(new Date());
            } catch (err) {
                if (live) setError(err.message);
            } finally {
                if (live) setLoading(false);
            }
        })();
        return () => { live = false; };
    }, [semesterId, refreshTick]);

    const rooms = useMemo(() => {
        const term = search.trim().toLowerCase();
        return (data?.rooms ?? []).filter(r =>
            (level === 'all' || r.level === level)
            && (building === 'all' || r.building === building)
            && (!term
                || r.room.toLowerCase().includes(term)
                || r.building.toLowerCase().includes(term)));
    }, [data, search, level, building]);

    const summary = data?.summary;
    const filtered = rooms.length !== (data?.rooms?.length ?? 0);

    return (
        <div className="reg-page">
            <header className="reg-head">
                <div>
                    <h2>Room utilization analysis <LiveChip state={liveState} updatedAt={updatedAt} /></h2>
                    <p className="reg-sub">
                        How hard every teaching space is working this semester, measured against the
                        {' '}{data?.window?.label ?? 'Mon–Fri, 08:00–17:00 · 9 h/day · 45 h/week'}
                        {' '}schedulable week — so under-used rooms can be reclaimed for scheduling.
                    </p>
                </div>
                <div className="reg-controls">
                    <label className="reg-filter">
                        <span>Semester</span>
                        <select value={semesterId} onChange={e => setSemesterId(e.target.value)}>
                            {semesters.map(s => (
                                <option key={s.id} value={s.id}>{s.name}{s.isActive ? ' · active' : ''}</option>
                            ))}
                        </select>
                    </label>
                    <button
                        type="button"
                        className="btn btn-primary"
                        onClick={() => runExport('utilization', downloadRoomUtilizationWorkbook,
                            'Exported room utilization — Overview plus Monday–Friday breakdowns.')}
                        disabled={!semesterId || exporting !== null}
                        title="Overview plus a sheet per teaching day, with under-used rooms highlighted"
                    >
                        {exporting === 'utilization' && <span className="spinner" aria-hidden="true" />}
                        Utilization .xlsx
                    </button>
                    <button
                        type="button"
                        className="btn"
                        onClick={() => runExport('grid', downloadRoomGridSchedule,
                            'Exported the room grid schedule — a sheet per day, print-ready.')}
                        disabled={!semesterId || exporting !== null}
                        title="Visual timetable: time slots against room columns, one sheet per day"
                    >
                        {exporting === 'grid' && <span className="spinner" aria-hidden="true" />}
                        Grid schedule .xlsx
                    </button>
                </div>
            </header>

            {error && <div className="alert">{error}</div>}

            <WindowNote window={data?.window} />

            {summary && (
                <section className="rua-tiles" aria-label="Utilization summary">
                    <Tile label="Total rooms" value={summary.totalRooms} />
                    <Tile label="Average utilization" value={summary.averageUtilizationPct} suffix="%" />
                    <Tile label="Critical" value={summary.critical} tone="critical" hint={levels[0].hint} />
                    <Tile label="Low usage" value={summary.low} tone="low" hint={levels[1].hint} />
                    <Tile label="Moderate" value={summary.moderate} tone="moderate" hint={levels[2].hint} />
                </section>
            )}

            <div className="rua-filters">
                <div className="rua-search">
                    <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor"
                        strokeWidth="2" strokeLinecap="round" aria-hidden="true">
                        <path d="M11 19a8 8 0 1 0 0-16 8 8 0 0 0 0 16M21 21l-4.35-4.35" />
                    </svg>
                    <input
                        type="text"
                        placeholder="Search by room or building…"
                        aria-label="Search rooms"
                        value={search}
                        onChange={e => setSearch(e.target.value)}
                    />
                </div>
                <label className="reg-filter">
                    <span>Utilization</span>
                    <select value={level} onChange={e => setLevel(e.target.value)}>
                        <option value="all">All levels</option>
                        {levels.map(l => <option key={l.key} value={l.key}>{l.label}</option>)}
                    </select>
                </label>
                <label className="reg-filter">
                    <span>Building</span>
                    <select value={building} onChange={e => setBuilding(e.target.value)}>
                        <option value="all">All buildings</option>
                        {(data?.buildings ?? []).map(b => <option key={b} value={b}>{b}</option>)}
                    </select>
                </label>
                {filtered && (
                    <button
                        type="button"
                        className="btn btn-ghost"
                        onClick={() => { setSearch(''); setLevel('all'); setBuilding('all'); }}
                    >
                        Clear filters
                    </button>
                )}
            </div>

            {loading && <p className="rua-empty">Loading room analysis…</p>}

            {!loading && rooms.length === 0 && (
                <p className="rua-empty">
                    {data?.rooms?.length
                        ? 'No rooms match these filters.'
                        : 'No rooms are configured yet — add them under Academic setup → Rooms.'}
                </p>
            )}

            {rooms.length > 0 && (
                <>
                    <p className="rua-count">
                        Showing {rooms.length} of {data.rooms.length} rooms
                        {level !== 'all' && <> · {levelLabel(level)}</>}
                    </p>
                    <div className="rua-grid">
                        {rooms.map(r => <RoomCard key={r.id} room={r} />)}
                    </div>
                </>
            )}
        </div>
    );
}

export default RoomUtilizationPage;
