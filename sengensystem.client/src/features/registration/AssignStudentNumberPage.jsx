import { useEffect, useState } from 'react';
import { listAssignableRegistrations, assignStudentNumber } from './api';
import { notifySuccess, notifyError } from '../shell/notify';
import { formatPHT, humanize } from './options';
import { useServerTable } from '../shell/useServerTable';
import { SortHeader, Pagination } from '../shell/tableControls';
import './registration.css';

// Admission Officer records the official student number — issued by the separate student-records
// system — against a SIS registration. SEN-GEN only issues the registration number; this closes
// the loop once the enrollee has been given their real student number elsewhere.
//
// The view switches between the work queue (still to number), the students already numbered — so
// an officer can confirm or correct one without hunting for it — and everything at once.
const views = [
    { value: 'pending', label: 'Still to number' },
    { value: 'numbered', label: 'Already numbered' },
    { value: 'all', label: 'All registrations' }
];

function AssignStudentNumberPage() {
    const [rows, setRows] = useState([]);
    const [total, setTotal] = useState(0);
    const [counts, setCounts] = useState({ numberedCount: 0, pendingCount: 0, totalCount: 0 });
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState(null);
    const [search, setSearch] = useState('');
    const [query, setQuery] = useState('');
    const [view, setView] = useState('pending');
    // Per-row draft input + in-flight flag, keyed by registration id.
    const [drafts, setDrafts] = useState({});
    const [busyId, setBusyId] = useState(null);

    // View, search, sort, and page are all decided server-side. Drafts are keyed by registration
    // id, so an in-progress entry survives sorting and paging untouched.
    const table = useServerTable({
        rows,
        total,
        initialSort: { key: 'registrationNumber', dir: 'asc' },
        search: query
    });

    useEffect(() => {
        let active = true;
        (async () => {
            setLoading(true);
            setError(null);
            try {
                const data = await listAssignableRegistrations({ status: view, ...table.query });
                if (!active) return;
                setRows(data.registrations);
                setTotal(data.total);
                setCounts({
                    numberedCount: data.numberedCount ?? 0,
                    pendingCount: data.pendingCount ?? 0,
                    totalCount: data.totalCount ?? 0
                });
                setDrafts(Object.fromEntries(
                    data.registrations.map(r => [r.id, r.officialStudentNumber ?? ''])
                ));
            } catch (err) {
                if (active) setError(err.message);
            } finally {
                if (active) setLoading(false);
            }
        })();
        return () => { active = false; };
    // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [view, table.queryKey]);

    function submitSearch(e) {
        e.preventDefault();
        setQuery(search.trim());
    }

    async function save(id) {
        const value = (drafts[id] ?? '').trim();
        if (!value) {
            notifyError('Enter the student number first.');
            return;
        }
        setBusyId(id);
        try {
            const updated = await assignStudentNumber(id, value);
            setRows(prev => prev.map(r => (r.id === id ? updated : r)));
            setDrafts(prev => ({ ...prev, [id]: updated.officialStudentNumber ?? '' }));
            notifySuccess(`Student number saved for ${updated.fullName}.`);
        } catch (err) {
            notifyError(err.message);
        } finally {
            setBusyId(null);
        }
    }

    return (
        <div className="reg-page">
            <header className="reg-head">
                <div>
                    <h2>Assign student number</h2>
                    <p className="reg-sub">
                        SEN-GEN issues a registration number; the official student number comes from the
                        separate student-records system. Once an enrollee has been given theirs, record it
                        here against their registration.
                    </p>
                </div>
                <div className="reg-controls">
                    <form className="reg-search" onSubmit={submitSearch}>
                        <input
                            type="search"
                            value={search}
                            onChange={e => setSearch(e.target.value)}
                            placeholder="Registration no., name, or student no.…"
                        />
                    </form>
                    <label className="reg-filter">
                        <span>Show</span>
                        <select value={view} onChange={e => setView(e.target.value)} disabled={!!query}>
                            {views.map(v => <option key={v.value} value={v.value}>{v.label}</option>)}
                        </select>
                    </label>
                </div>
            </header>

            <p className="reg-sub" style={{ marginTop: '-0.6rem' }}>
                {query
                    ? `Showing every registration matching “${query}”, numbered or not.`
                    : view === 'numbered'
                        ? 'Showing registrations that already have a student number on file.'
                        : view === 'all'
                            ? 'Showing every registration, numbered or not.'
                            : 'Showing registrations that still need a student number.'}
                {' '}
                <strong>{counts.numberedCount}</strong> of {counts.totalCount} numbered
                · <strong>{counts.pendingCount}</strong> still pending.
            </p>

            {error && <div className="alert">{error}</div>}

            {loading ? (
                <p className="reg-empty">Loading…</p>
            ) : table.total === 0 ? (
                <p className="reg-empty">
                    {query ? 'No registrations match your search.'
                        : view === 'numbered' ? 'No registration has a student number on file yet.'
                            : view === 'all' ? 'There are no registrations yet.'
                                : 'Every registration has a student number on file.'}
                </p>
            ) : (
                <div className="card reg-table-wrap">
                    <table className="reg-table">
                        <thead>
                            <tr>
                                <SortHeader label="Registration no." sortKey="registrationNumber" sort={table.sort} onSort={table.toggleSort} />
                                <SortHeader label="Name" sortKey="fullName" sort={table.sort} onSort={table.toggleSort} />
                                <SortHeader label="Program" sortKey="program" sort={table.sort} onSort={table.toggleSort} />
                                <SortHeader label="SIS status" sortKey="status" sort={table.sort} onSort={table.toggleSort} />
                                <SortHeader label="Numbering" sortKey="numbering" sort={table.sort} onSort={table.toggleSort} />
                                <th>Student number</th>
                                <th></th>
                            </tr>
                        </thead>
                        <tbody>
                            {table.pageRows.map(r => {
                                const draft = drafts[r.id] ?? '';
                                const unchanged = draft.trim() === (r.officialStudentNumber ?? '');
                                return (
                                    <tr key={r.id}>
                                        <td className="reg-mono">{r.registrationNumber}</td>
                                        <td><strong>{r.fullName}</strong></td>
                                        <td>{r.program}</td>
                                        <td>
                                            <span className={r.status === 'Confirmed' ? 'chip chip-blue' : 'chip chip-muted'}>
                                                {humanize(r.status)}
                                            </span>
                                        </td>
                                        <td>
                                            {r.officialStudentNumber
                                                ? <span className="chip chip-blue">Numbered</span>
                                                : <span className="chip chip-yellow">Pending</span>}
                                        </td>
                                        <td>
                                            <input
                                                className="reg-inline-input"
                                                type="text"
                                                value={draft}
                                                placeholder="e.g. 02000123456"
                                                onChange={e => setDrafts(prev => ({ ...prev, [r.id]: e.target.value }))}
                                                onKeyDown={e => { if (e.key === 'Enter') save(r.id); }}
                                            />
                                            {r.officialStudentNumberSetAtUtc && (
                                                <span className="reg-when">Recorded {formatPHT(r.officialStudentNumberSetAtUtc)}</span>
                                            )}
                                        </td>
                                        <td className="reg-actions">
                                            <button
                                                className="btn btn-primary btn-sm"
                                                disabled={busyId === r.id || unchanged}
                                                onClick={() => save(r.id)}
                                            >
                                                {busyId === r.id ? 'Saving…' : r.officialStudentNumber ? 'Update' : 'Save'}
                                            </button>
                                        </td>
                                    </tr>
                                );
                            })}
                        </tbody>
                    </table>
                    <Pagination {...table} />
                </div>
            )}
        </div>
    );
}

export default AssignStudentNumberPage;
