import { useEffect, useRef, useState } from 'react';
import { getAuditTrail } from './api';
import { subscribeToReports } from '../reports/live';
import { useServerTable } from '../shell/useServerTable';
import { SortHeader, Pagination, TableSearch } from '../shell/tableControls';
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
    'SystemParametersExported',
    'SectionCapacityCapChanged', 'TimeSlotSaved', 'FacultyLoadLimitChanged'
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
    const [total, setTotal] = useState(0);
    // Supplied by the server so the dropdown lists every action in the trail, not just the ones
    // that happen to be on the page being viewed.
    const [actions, setActions] = useState(['All']);
    const [loading, setLoading] = useState(true);
    const [refreshing, setRefreshing] = useState(false);
    const [error, setError] = useState(null);
    const [filter, setFilter] = useState('All');
    const [search, setSearch] = useState('');
    const [appliedSearch, setAppliedSearch] = useState('');

    // The trail is the longest table in the system and is read backwards from "what happened just
    // now", so it opens newest-first and the server does the filtering, searching, and paging —
    // otherwise an event more than one page back could not be found at all.
    const table = useServerTable({
        rows: entries,
        total,
        initialSort: { key: 'occurredAtUtc', dir: 'desc' },
        search: appliedSearch
    });

    // Searching is a round trip now, so the box is debounced — one request when typing settles,
    // rather than one per keystroke.
    useEffect(() => {
        const id = setTimeout(() => setAppliedSearch(search.trim()), 300);
        return () => clearTimeout(id);
    }, [search]);

    // Held in a ref so the live-tail subscription below can refetch the *current* view without
    // having to re-subscribe every time the page, sort, or filter changes.
    const queryRef = useRef(null);
    useEffect(() => {
        queryRef.current = { action: filter, ...table.query };
    // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [filter, table.queryKey]);

    async function load(isRefresh) {
        if (isRefresh) setRefreshing(true);
        setError(null);
        try {
            const data = await getAuditTrail(queryRef.current);
            setEntries(data.entries);
            setTotal(data.total);
            if (data.actions) setActions(['All', ...data.actions]);
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
                const data = await getAuditTrail({ action: filter, ...table.query });
                if (!active) return;
                setEntries(data.entries);
                setTotal(data.total);
                if (data.actions) setActions(['All', ...data.actions]);
            } catch (err) {
                if (active) setError(err.message);
            } finally {
                if (active) setLoading(false);
            }
        })();
        return () => { active = false; };
    // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [filter, table.queryKey]);

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
                        // Refetches whatever the user is currently looking at (via queryRef),
                        // rather than resetting them to page 1 of an unfiltered trail.
                        const data = await getAuditTrail(queryRef.current);
                        setEntries(data.entries);
                        setTotal(data.total);
                        if (data.actions) setActions(['All', ...data.actions]);
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
                    <TableSearch
                        value={search} onChange={setSearch}
                        placeholder="Filter actor, detail, or IP…"
                    />
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
            ) : table.total === 0 ? (
                <p className="audit-empty">
                    {search
                        ? 'No audit entries match your filter.'
                        : `No audit entries${filter === 'All' ? ' yet' : ' for this action'}.`}
                </p>
            ) : (
                <div className="card audit-table-wrap">
                    <table className="audit-table">
                        <thead>
                            <tr>
                                <SortHeader label="When" sortKey="occurredAtUtc" sort={table.sort} onSort={table.toggleSort} />
                                <SortHeader label="Actor" sortKey="actorName" sort={table.sort} onSort={table.toggleSort} />
                                <SortHeader label="Action" sortKey="action" sort={table.sort} onSort={table.toggleSort} />
                                <SortHeader label="Detail" sortKey="summary" sort={table.sort} onSort={table.toggleSort} />
                                <SortHeader label="Source" sortKey="ipAddress" sort={table.sort} onSort={table.toggleSort} />
                            </tr>
                        </thead>
                        <tbody>
                            {table.pageRows.map(e => (
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
                    <Pagination {...table} />
                </div>
            )}
        </div>
    );
}

export default AuditTrailPage;
