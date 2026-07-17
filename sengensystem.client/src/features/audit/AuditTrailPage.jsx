import { useEffect, useMemo, useRef, useState } from 'react';
import { getAuditTrail } from './api';
import { subscribeToReports } from '../reports/live';
import { LiveChip } from '../reports/ReportsPage';
import '../reports/reports.css'; // LiveChip styles
import './audit.css';

// "AccountRegistered" -> "Account registered"
function humanize(action) {
    const spaced = action.replace(/([a-z])([A-Z])/g, '$1 $2');
    return spaced.charAt(0).toUpperCase() + spaced.slice(1).toLowerCase();
}

// Group actions into a colour so the log scans quickly. Yellow flags the
// security-sensitive events an admin scans for first.
const yellowActions = new Set([
    'PasswordChanged', 'UserAccountDeactivated', 'LoginFailed', 'ScheduleGenerationFailed'
]);
const blueActions = new Set([
    'AccountRegistered', 'UserAccountCreated', 'UserAccountUpdated',
    'ScheduleGenerated', 'SchedulePublished', 'ScheduleOverridden',
    'SlotApproved', 'SlotRequested', 'LoginSucceeded',
    'SchoolYearSaved', 'SemesterSaved', 'BuildingSaved', 'RoomSaved',
    'CurriculumSaved', 'SubjectSaved', 'FacultyLoadSaved',
    'SubjectArchived', 'SubjectRestored', 'ScheduleArchived', 'SemesterExported',
    'SystemParametersExported'
]);

function actionChip(action) {
    if (yellowActions.has(action)) return 'chip chip-yellow';
    if (blueActions.has(action)) return 'chip chip-blue';
    return 'chip chip-muted';
}

// SEN-GEN serves STI Alaminos, so the trail always reads in Philippine time (UTC+8),
// regardless of the viewer's device time zone. Timestamps arrive as UTC ISO strings.
function formatWhen(iso) {
    const d = new Date(iso);
    if (Number.isNaN(d.getTime())) return iso;
    return d.toLocaleString('en-PH', {
        timeZone: 'Asia/Manila',
        year: 'numeric', month: 'short', day: '2-digit',
        hour: '2-digit', minute: '2-digit', second: '2-digit',
        hour12: true, timeZoneName: 'short'
    });
}

function AuditTrailPage() {
    const [entries, setEntries] = useState([]);
    const [loading, setLoading] = useState(true);
    const [refreshing, setRefreshing] = useState(false);
    const [error, setError] = useState(null);
    const [filter, setFilter] = useState('All');

    async function load(isRefresh) {
        if (isRefresh) setRefreshing(true);
        setError(null);
        try {
            const data = await getAuditTrail();
            setEntries(data.entries);
        } catch (err) {
            setError(err.message);
        } finally {
            setLoading(false);
            setRefreshing(false);
        }
    }

    useEffect(() => {
        let active = true;
        (async () => {
            try {
                const data = await getAuditTrail();
                if (active) setEntries(data.entries);
            } catch (err) {
                if (active) setError(err.message);
            } finally {
                if (active) setLoading(false);
            }
        })();
        return () => { active = false; };
    }, []);

    // Live tail: every audited action on the server pushes a SignalR signal — new rows
    // appear without touching Refresh.
    const [liveState, setLiveState] = useState('offline');
    const [updatedAt, setUpdatedAt] = useState(null);
    const debounceRef = useRef(null);
    useEffect(() => {
        const unsubscribe = subscribeToReports(
            () => {
                clearTimeout(debounceRef.current);
                debounceRef.current = setTimeout(async () => {
                    try {
                        const data = await getAuditTrail();
                        setEntries(data.entries);
                        setUpdatedAt(new Date());
                    } catch {
                        // keep showing the last good list; the Refresh button still works
                    }
                }, 500);
            },
            state => setLiveState(state));
        return () => {
            clearTimeout(debounceRef.current);
            unsubscribe();
        };
    }, []);

    // Distinct actions present, for the filter dropdown.
    const actions = useMemo(() => {
        const set = new Set(entries.map(e => e.action));
        return ['All', ...[...set].sort()];
    }, [entries]);

    const visible = filter === 'All' ? entries : entries.filter(e => e.action === filter);

    return (
        <div className="audit-page">
            <header className="audit-head">
                <div>
                    <h2>Audit trail</h2>
                    <p className="audit-sub">
                        Accountability log of security- and data-relevant actions across SEN-GEN —
                        sign-ins, registrations, profile and password changes, and schedule generation.
                    </p>
                </div>
                <div className="audit-controls">
                    <LiveChip state={liveState} updatedAt={updatedAt} />
                    <label className="audit-filter">
                        <span>Action</span>
                        <select value={filter} onChange={e => setFilter(e.target.value)}>
                            {actions.map(a => (
                                <option key={a} value={a}>{a === 'All' ? 'All actions' : humanize(a)}</option>
                            ))}
                        </select>
                    </label>
                    <button className="btn btn-ghost" type="button" onClick={() => load(true)} disabled={refreshing}>
                        {refreshing && <span className="spinner" aria-hidden="true" />}
                        {refreshing ? 'Refreshing…' : 'Refresh'}
                    </button>
                </div>
            </header>

            {error && <div className="alert">{error}</div>}

            {loading ? (
                <p className="audit-empty">Loading audit trail…</p>
            ) : visible.length === 0 ? (
                <p className="audit-empty">No audit entries{filter === 'All' ? ' yet' : ' for this action'}.</p>
            ) : (
                <div className="card audit-table-wrap">
                    <table className="audit-table">
                        <thead>
                            <tr>
                                <th>When</th>
                                <th>Actor</th>
                                <th>Action</th>
                                <th>Detail</th>
                                <th>Source</th>
                            </tr>
                        </thead>
                        <tbody>
                            {visible.map(e => (
                                <tr key={e.id}>
                                    <td className="audit-when">{formatWhen(e.occurredAtUtc)}</td>
                                    <td>
                                        <strong>{e.actorName}</strong>
                                        <span className="audit-role">{e.actorRole}</span>
                                    </td>
                                    <td><span className={actionChip(e.action)}>{humanize(e.action)}</span></td>
                                    <td className="audit-detail">{e.summary}</td>
                                    <td className="audit-mono">{e.ipAddress || '—'}</td>
                                </tr>
                            ))}
                        </tbody>
                    </table>
                </div>
            )}
        </div>
    );
}

export default AuditTrailPage;
