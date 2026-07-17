import { useEffect, useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import { getToken } from '../auth/api';
import { useAuth } from '../auth/useAuth';
import { getDashboardMetrics } from '../dashboard/api';
import { subscribeToReports } from './live';
import { downloadSemesterExport, downloadSystemParametersExport } from './exportApi';
import { notifySuccess, notifyError } from '../shell/notify';
import '../registration/registration.css';
import './reports.css';

/* FR-RPT: semester-scoped operational reports, viewable inline and exportable as .xlsx
   for institutional planning (FR-RPT-02). Live: a SignalR push re-builds the open report
   the moment its underlying data changes. */

const reports = [
    { key: 'registration', title: 'Validated registrations', blurb: 'Every SIS registration with status, checklist, and clearance.' },
    { key: 'enlistment', title: 'Enlistment results', blurb: 'Seats taken per section with pending and rejected requests.' },
    { key: 'faculty-load', title: 'Faculty load summary', blurb: 'Assigned units vs. each member’s ceiling and scheduled hours.' },
    { key: 'room-utilization', title: 'Room utilization', blurb: 'Room usage within the institutional 08:00–17:00 window.' },
    { key: 'document-completion', title: 'Document completion', blurb: 'Checklist completion per enrollee with missing papers.' }
];

export function LiveChip({ state, updatedAt }) {
    const label = state === 'live' ? 'Live' : state === 'reconnecting' ? 'Reconnecting…' : 'Offline';
    return (
        <span className={`chip live-chip ${state === 'live' ? 'on' : ''}`} title="Real-time updates via SignalR">
            <span className="live-dot" aria-hidden="true" />
            {label}
            {updatedAt && state === 'live' && (
                <small>· updated {updatedAt.toLocaleTimeString()}</small>
            )}
        </span>
    );
}

function ReportsPage() {
    const { user } = useAuth();
    const [semesters, setSemesters] = useState([]);
    const [semesterId, setSemesterId] = useState('');
    const [active, setActive] = useState('registration');
    const [data, setData] = useState(null);
    const [loading, setLoading] = useState(false);
    const [error, setError] = useState(null);
    const [liveState, setLiveState] = useState('offline');
    const [updatedAt, setUpdatedAt] = useState(null);
    const [refreshTick, setRefreshTick] = useState(0);
    const [bundleBusy, setBundleBusy] = useState(false);
    const [sysBusy, setSysBusy] = useState(false);
    const debounceRef = useRef(null);

    // The semester selector piggybacks on the dashboard metrics payload.
    useEffect(() => {
        getDashboardMetrics()
            .then(d => {
                setSemesters(d.semesters ?? []);
                if (d.semesterId) setSemesterId(d.semesterId);
            })
            .catch(err => setError(err.message));
    }, []);

    // Real-time refresh: any report-relevant mutation on the server re-builds the open report.
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
        let activeFlag = true;
        (async () => {
            setLoading(refreshTick === 0); // live refreshes swap data silently, without a flash
            setError(null);
            try {
                const response = await fetch(
                    `/api/reports/${active}?semesterId=${encodeURIComponent(semesterId)}`,
                    { headers: { Authorization: `Bearer ${getToken()}` } });
                const payload = await response.json();
                if (!response.ok) throw new Error(payload?.message || 'Could not load the report.');
                if (activeFlag) {
                    setData(payload);
                    setUpdatedAt(new Date());
                }
            } catch (err) {
                if (activeFlag) setError(err.message);
            } finally {
                if (activeFlag) setLoading(false);
            }
        })();
        return () => { activeFlag = false; };
    }, [active, semesterId, refreshTick]);

    async function exportXlsx() {
        setError(null);
        try {
            const response = await fetch(
                `/api/reports/${active}?semesterId=${encodeURIComponent(semesterId)}&format=xlsx`,
                { headers: { Authorization: `Bearer ${getToken()}` } });
            if (!response.ok) throw new Error('Export failed.');
            const blob = await response.blob();
            const url = URL.createObjectURL(blob);
            const a = document.createElement('a');
            a.href = url;
            a.download = `sengen-${active}.xlsx`;
            a.click();
            URL.revokeObjectURL(url);
            notifySuccess(`Exported ${reports.find(r => r.key === active)?.title ?? active} to Excel.`);
        } catch (err) {
            setError(err.message);
            notifyError(err.message);
        }
    }

    // The "archive the data" path: every report for the semester in one workbook.
    async function exportBundle() {
        setError(null);
        setBundleBusy(true);
        try {
            await downloadSemesterExport(semesterId);
            notifySuccess('Exported the full semester bundle — all reports in one workbook.');
        } catch (err) {
            setError(err.message);
            notifyError(err.message);
        } finally {
            setBundleBusy(false);
        }
    }

    // Admin-only: the setup/master data (school years, rooms, time slots, curricula,
    // blocks, faculty, accounts) in one workbook.
    async function exportSystemParameters() {
        setError(null);
        setSysBusy(true);
        try {
            await downloadSystemParametersExport();
            notifySuccess('Exported the system parameters workbook.');
        } catch (err) {
            setError(err.message);
            notifyError(err.message);
        } finally {
            setSysBusy(false);
        }
    }

    return (
        <div className="reg-page">
            <header className="reg-head">
                <div>
                    <h2>Reports &amp; analytics <LiveChip state={liveState} updatedAt={updatedAt} /></h2>
                    <p className="reg-sub">
                        Semester-scoped reports for institutional planning — live-updating, viewable inline,
                        exportable to Excel. Need per-faculty workbooks?{' '}
                        <Link to="/reports/faculty-load">Faculty load reports</Link>.
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
                    <button className="btn btn-primary" type="button" onClick={exportXlsx} disabled={!data || loading}>
                        Export .xlsx
                    </button>
                    <button
                        className="btn"
                        type="button"
                        onClick={exportBundle}
                        disabled={!semesterId || bundleBusy}
                        title="Everything for this semester — registrations, master schedule, faculty loads, enlistment, room utilization, and documents — in one workbook"
                    >
                        {bundleBusy && <span className="spinner" aria-hidden="true" />}
                        {bundleBusy ? 'Bundling…' : 'Full bundle'}
                    </button>
                    {user?.role === 'SchoolAdmin' && (
                        <button
                            className="btn"
                            type="button"
                            onClick={exportSystemParameters}
                            disabled={sysBusy}
                            title="The setup data the system runs on — academic calendar, buildings & rooms, time slots, curricula & subjects, class sections, faculty, and user accounts — in one workbook"
                        >
                            {sysBusy && <span className="spinner" aria-hidden="true" />}
                            {sysBusy ? 'Exporting…' : 'System parameters'}
                        </button>
                    )}
                </div>
            </header>

            {error && <div className="alert">{error}</div>}

            <div style={{ display: 'flex', gap: '0.4rem', flexWrap: 'wrap', marginBottom: '1.1rem' }}>
                {reports.map(r => (
                    <button
                        key={r.key}
                        type="button"
                        className={active === r.key ? 'btn btn-primary' : 'btn'}
                        title={r.blurb}
                        onClick={() => setActive(r.key)}
                    >
                        {r.title}
                    </button>
                ))}
            </div>

            {loading ? (
                <p className="reg-empty">Building report…</p>
            ) : !data ? (
                <p className="reg-empty">Pick a semester to build the report.</p>
            ) : data.rows.length === 0 ? (
                <p className="reg-empty">No rows for this semester.</p>
            ) : (
                <div className="card reg-table-wrap">
                    <table className="reg-table">
                        <thead>
                            <tr>{data.headers.map(h => <th key={h}>{h}</th>)}</tr>
                        </thead>
                        <tbody>
                            {data.rows.map((row, i) => (
                                <tr key={i}>
                                    {row.map((cell, j) => (
                                        <td key={j} style={{ whiteSpace: j === row.length - 1 ? 'normal' : undefined }}>
                                            {String(cell)}
                                        </td>
                                    ))}
                                </tr>
                            ))}
                        </tbody>
                    </table>
                </div>
            )}

            <p style={{ marginTop: '0.9rem', fontSize: '0.8rem', color: 'var(--text-3)' }}>
                {data ? `${data.count} row(s) — ${data.title} · ${data.semesterName}` : ''}
            </p>
        </div>
    );
}

export default ReportsPage;
