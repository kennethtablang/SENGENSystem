import { useEffect, useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import { getToken } from '../auth/api';
import { getDashboardMetrics } from '../dashboard/api';
import { subscribeToReports } from './live';
import { LiveChip } from './ReportsPage';
import { notifySuccess, notifyError } from '../shell/notify';
import { saveBlob, filenameFromDisposition } from '../shell/download';
import { useTableControls } from '../shell/useTableControls';
import { SortHeader, Pagination } from '../shell/tableControls';
import '../registration/registration.css';
import './reports.css';

/* Faculty Academic Load Reports: monitor teaching assignments per semester, search by
   name or employee ID, and download individual, consolidated, grid,
   or bulk (.zip) workbooks for workload balance and institutional compliance. */

async function downloadFile(url, fallbackName) {
    const response = await fetch(url, { headers: { Authorization: `Bearer ${getToken()}` } });
    if (!response.ok) {
        let message = 'Download failed.';
        try {
            message = (await response.json())?.message ?? message;
        } catch {
            // non-JSON error body
        }
        throw new Error(message);
    }
    const blob = await response.blob();
    const name = filenameFromDisposition(response.headers.get('Content-Disposition'), fallbackName);
    saveBlob(blob, name);
}

function FacultyLoadReportsPage() {
    const [semesters, setSemesters] = useState([]);
    const [semesterId, setSemesterId] = useState('');
    const [search, setSearch] = useState('');
    const [data, setData] = useState(null);
    const [error, setError] = useState(null);
    const [busy, setBusy] = useState(null); // key of the running download
    const [liveState, setLiveState] = useState('offline');
    const [updatedAt, setUpdatedAt] = useState(null);
    const [refreshTick, setRefreshTick] = useState(0);
    const debounceRef = useRef(null);

    useEffect(() => {
        getDashboardMetrics()
            .then(d => {
                setSemesters(d.semesters ?? []);
                if (d.semesterId) setSemesterId(d.semesterId);
            })
            .catch(err => setError(err.message));
    }, []);

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
        if (!semesterId) return;
        let live = true;
        const run = setTimeout(async () => {
            setError(null);
            try {
                const qs = new URLSearchParams({ semesterId });
                if (search.trim()) qs.set('search', search.trim());
                const response = await fetch(`/api/reports/faculty-loading?${qs}`, {
                    headers: { Authorization: `Bearer ${getToken()}` }
                });
                const payload = await response.json();
                if (!response.ok) throw new Error(payload?.message || 'Could not load faculty loading.');
                if (live) {
                    setData(payload);
                    setUpdatedAt(new Date());
                }
            } catch (err) {
                if (live) setError(err.message);
            }
        }, search ? 250 : 0); // debounce keystrokes
        return () => {
            live = false;
            clearTimeout(run);
        };
    }, [semesterId, search, refreshTick]);

    async function download(key, url, fallbackName, doneMessage) {
        setBusy(key);
        try {
            await downloadFile(url, fallbackName);
            notifySuccess(doneMessage);
        } catch (err) {
            notifyError(err.message);
        } finally {
            setBusy(null);
        }
    }

    const qsSem = `semesterId=${encodeURIComponent(semesterId)}`;
    const rows = data?.faculty ?? [];

    // Standing and units are the columns this report exists to be sorted by — "who is overloaded"
    // is one click rather than a read-through.
    const table = useTableControls(rows, {
        columns: {
            name: f => f.name,
            employeeId: f => f.employeeId,
            programCode: f => f.programCode,
            totalUnits: f => f.totalUnits,
            totalSubjects: f => f.totalSubjects,
            scheduledHours: f => f.scheduledHours,
            // Overloaded first — the rows that need acting on lead the list.
            standing: f => (f.standing === 'Overloaded' ? 0 : f.standing === 'Unassigned' ? 2 : 1)
        },
        initialSort: { key: 'name', dir: 'asc' }
    });

    return (
        <div className="reg-page">
            <header className="reg-head">
                <div>
                    <h2>Faculty load reports <LiveChip state={liveState} updatedAt={updatedAt} /></h2>
                    <p className="reg-sub">
                        Teaching assignments by semester — monitor workload balance and download
                        compliance reports per member, consolidated, or in bulk. Looking for room
                        usage? <Link to="/analytics/room-utilization">Room utilization</Link>.
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
                </div>
            </header>

            {error && <div className="alert">{error}</div>}

            <div className="flr-toolbar">
                <div className="flr-search">
                    <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor"
                        strokeWidth="2" strokeLinecap="round" aria-hidden="true">
                        <path d="M11 19a8 8 0 1 0 0-16 8 8 0 0 0 0 16M21 21l-4.35-4.35" />
                    </svg>
                    <input
                        type="text"
                        placeholder="Search by faculty name or employee ID…"
                        aria-label="Search faculty"
                        value={search}
                        onChange={e => setSearch(e.target.value)}
                    />
                </div>
                <button
                    type="button" className="btn btn-primary" disabled={!semesterId || busy !== null}
                    onClick={() => download('bulk', `/api/reports/faculty-loading/bulk?${qsSem}`,
                        'sengen-faculty-load-reports.zip', 'Downloaded the full report bundle.')}
                >
                    {busy === 'bulk' && <span className="spinner" aria-hidden="true" />}
                    Download all (.zip)
                </button>
                <button
                    type="button" className="btn btn-primary" disabled={!semesterId || busy !== null}
                    title="Confirmation of Faculty Loading — one memo per faculty member (PDF)"
                    onClick={() => download('bulkPdf', `/api/reports/faculty-loading/consolidated.pdf?${qsSem}`,
                        'sengen-confirmation-faculty-loading.pdf',
                        'Downloaded the Confirmation of Faculty Loading (PDF).')}
                >
                    {busy === 'bulkPdf' && <span className="spinner" aria-hidden="true" />}
                    Confirmation of Loading (PDF)
                </button>
                <button
                    type="button" className="btn btn-ghost" disabled={!semesterId || busy !== null}
                    title="Confirmation of Faculty Loading — one worksheet per faculty member (Excel)"
                    onClick={() => download('consolidated', `/api/reports/faculty-loading/consolidated?${qsSem}`,
                        'sengen-confirmation-faculty-loading.xlsx', 'Downloaded the Confirmation of Faculty Loading (Excel).')}
                >
                    {busy === 'consolidated' && <span className="spinner" aria-hidden="true" />}
                    Confirmation of Loading (Excel)
                </button>
                <button
                    type="button" className="btn btn-ghost" disabled={!semesterId || busy !== null}
                    onClick={() => download('grids', `/api/reports/grid-schedules?${qsSem}`,
                        'sengen-grid-schedules.xlsx', 'Downloaded the grid schedules.')}
                >
                    {busy === 'grids' && <span className="spinner" aria-hidden="true" />}
                    Grid schedules
                </button>
            </div>

            {!data ? (
                <p className="reg-empty">Pick a semester to load faculty assignments.</p>
            ) : rows.length === 0 ? (
                <p className="reg-empty">No faculty match this search.</p>
            ) : (
                <div className="card reg-table-wrap">
                    <table className="reg-table">
                        <thead>
                            <tr>
                                <SortHeader label="Faculty" sortKey="name" sort={table.sort} onSort={table.toggleSort} />
                                <SortHeader label="Employee ID" sortKey="employeeId" sort={table.sort} onSort={table.toggleSort} />
                                <SortHeader label="Program" sortKey="programCode" sort={table.sort} onSort={table.toggleSort} />
                                <SortHeader label="Total units" sortKey="totalUnits" sort={table.sort} onSort={table.toggleSort} />
                                <SortHeader label="Total subjects" sortKey="totalSubjects" sort={table.sort} onSort={table.toggleSort} />
                                <SortHeader label="Scheduled h/week" sortKey="scheduledHours" sort={table.sort} onSort={table.toggleSort} />
                                <SortHeader label="Standing" sortKey="standing" sort={table.sort} onSort={table.toggleSort} />
                                <th></th>
                            </tr>
                        </thead>
                        <tbody>
                            {table.pageRows.map(f => {
                                const over = f.totalUnits > f.maxUnits;
                                const pct = f.maxUnits === 0 ? 0 : Math.min(100, Math.round(100 * f.totalUnits / f.maxUnits));
                                return (
                                    <tr key={f.facultyProfileId}>
                                        <td>{f.name}</td>
                                        <td style={{ fontFamily: 'var(--mono)' }}>{f.employeeId || '—'}</td>
                                        <td>{f.programCode}</td>
                                        <td>
                                            <span className="flr-units">
                                                {f.totalUnits}/{f.maxUnits}
                                                <span className={`flr-bar${over ? ' over' : ''}`}>
                                                    <span style={{ width: `${pct}%` }} />
                                                </span>
                                            </span>
                                        </td>
                                        <td>{f.totalSubjects}</td>
                                        <td>{f.scheduledHours}</td>
                                        <td>
                                            <span className={`chip ${f.standing === 'Overloaded' ? 'chip-down'
                                                : f.standing === 'Unassigned' ? 'chip-muted' : 'chip-up'}`}>
                                                {f.standing}
                                            </span>
                                        </td>
                                        <td>
                                            <div className="flr-row-actions">
                                                <button
                                                    type="button" className="btn btn-ghost"
                                                    disabled={busy !== null}
                                                    onClick={() => download(f.facultyProfileId,
                                                        `/api/reports/faculty-loading/${f.facultyProfileId}?${qsSem}`,
                                                        'sengen-load-report.xlsx',
                                                        `Downloaded ${f.name}'s load report.`)}
                                                >
                                                    {busy === f.facultyProfileId && <span className="spinner" aria-hidden="true" />}
                                                    Excel
                                                </button>
                                                <button
                                                    type="button" className="btn btn-ghost"
                                                    disabled={busy !== null}
                                                    onClick={() => download(`${f.facultyProfileId}-pdf`,
                                                        `/api/reports/faculty-loading/${f.facultyProfileId}/pdf?${qsSem}`,
                                                        'sengen-load-report.pdf',
                                                        `Downloaded ${f.name}'s load report (PDF).`)}
                                                >
                                                    {busy === `${f.facultyProfileId}-pdf` && <span className="spinner" aria-hidden="true" />}
                                                    PDF
                                                </button>
                                                <button
                                                    type="button" className="btn btn-ghost"
                                                    disabled={busy !== null}
                                                    title="Weekly timetable (Mon–Sat) plus a daily class breakdown"
                                                    onClick={() => download(`${f.facultyProfileId}-grid`,
                                                        `/api/reports/faculty-loading/${f.facultyProfileId}/schedule-grid?${qsSem}`,
                                                        'sengen-schedule-grid.xlsx',
                                                        `Downloaded ${f.name}'s schedule grid.`)}
                                                >
                                                    {busy === `${f.facultyProfileId}-grid` && <span className="spinner" aria-hidden="true" />}
                                                    Schedule Grid
                                                </button>
                                            </div>
                                        </td>
                                    </tr>
                                );
                            })}
                        </tbody>
                    </table>
                    <Pagination {...table} />
                </div>
            )}

            <p style={{ marginTop: '0.9rem', fontSize: '0.8rem', color: 'var(--text-3)' }}>
                {data ? `${data.count} faculty member(s) · ${data.semesterName}` : ''}
            </p>
        </div>
    );
}

export default FacultyLoadReportsPage;
