import { useEffect, useState } from 'react';
import { listTermActivations, validateTermActivation } from './api';
import { notifySuccess, notifyError } from '../shell/notify';
import { formatPHT } from './options';
import { useServerTable } from '../shell/useServerTable';
import { SortHeader, Pagination } from '../shell/tableControls';
import './registration.css';

/* The sortable columns now live on the server (ListTermActivationsEndpoint) — including the rule
   this list used to encode, that the student is sorted on the number they actually identify
   themselves by, with the internal registration number as the fallback for anyone not yet issued
   one. Sorting here would only have ordered the page it was handed. */

const statusChip = {
    Pending: 'chip chip-muted',
    Validated: 'chip chip-blue',
    Rejected: 'chip chip-yellow'
};

function TermActivationsPage() {
    const [activations, setActivations] = useState([]);
    const [total, setTotal] = useState(0);
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState(null);
    const [filter, setFilter] = useState('Pending');
    const [busyId, setBusyId] = useState(null);
    const [reload, setReload] = useState(0);
    const table = useServerTable({
        rows: activations,
        total,
        initialSort: { key: 'requestedAtUtc', dir: 'desc' }
    });

    useEffect(() => {
        let active = true;
        (async () => {
            setLoading(true);
            setError(null);
            try {
                const data = await listTermActivations({ status: filter, ...table.query });
                if (!active) return;
                setActivations(data.activations);
                setTotal(data.total);
            } catch (err) {
                if (active) setError(err.message);
            } finally {
                if (active) setLoading(false);
            }
        })();
        return () => { active = false; };
    // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [filter, table.queryKey, reload]);

    async function act(id, approve) {
        setBusyId(id);
        setError(null);
        try {
            const remarks = approve ? null : (window.prompt('Reason for rejection (optional):') ?? '');
            // The server derives the year level (advance on a new school year, hold within one);
            // omitting it here keeps that derivation authoritative rather than second-guessing it.
            const result = await validateTermActivation(id, { approve, remarks });
            notifySuccess(approve
                ? `Term activation approved — enrolled as ${result.yearLevelLabel}.`
                : 'Term activation rejected.');
            setReload(r => r + 1);
        } catch (err) {
            setError(err.message);
            notifyError(err.message);
        } finally {
            setBusyId(null);
        }
    }

    return (
        <div className="reg-page">
            <header className="reg-head">
                <div>
                    <h2>Term activations</h2>
                    <p className="reg-sub">
                        Validate returning students' requests to activate for the current term. Approving one
                        emails the student a confirmation and settles the year level they come back into —
                        a student moves up a year when the school year turns over, and stays put within it.
                    </p>
                </div>
                <label className="reg-filter">
                    <span>Status</span>
                    <select value={filter} onChange={e => setFilter(e.target.value)}>
                        {['Pending', 'Validated', 'Rejected', 'All'].map(s => <option key={s} value={s}>{s}</option>)}
                    </select>
                </label>
            </header>

            {error && <div className="alert">{error}</div>}

            {loading ? (
                <p className="reg-empty">Loading…</p>
            ) : activations.length === 0 ? (
                <p className="reg-empty">No {filter === 'All' ? '' : filter.toLowerCase()} requests.</p>
            ) : (
                <div className="card reg-table-wrap">
                    <table className="reg-table">
                        <thead>
                            <tr>
                                <SortHeader label="Student no." sortKey="studentNumber" sort={table.sort} onSort={table.toggleSort} />
                                <SortHeader label="Name" sortKey="studentName" sort={table.sort} onSort={table.toggleSort} />
                                <SortHeader label="Program" sortKey="program" sort={table.sort} onSort={table.toggleSort} />
                                <SortHeader label="Year level" sortKey="yearLevel" sort={table.sort} onSort={table.toggleSort} />
                                <SortHeader label="Term" sortKey="semesterName" sort={table.sort} onSort={table.toggleSort} />
                                <SortHeader label="Requested" sortKey="requestedAtUtc" sort={table.sort} onSort={table.toggleSort} />
                                <SortHeader label="Status" sortKey="status" sort={table.sort} onSort={table.toggleSort} />
                                <th></th>
                            </tr>
                        </thead>
                        <tbody>
                            {table.pageRows.map(a => (
                                <tr key={a.id}>
                                    <td className="reg-mono">
                                        {a.officialStudentNumber || a.registrationNumber}
                                        {!a.officialStudentNumber && (
                                            <span className="reg-when">Registration no. — student no. not yet issued</span>
                                        )}
                                    </td>
                                    <td>
                                        <strong>{a.lastName || a.studentName}</strong>
                                        {a.lastName && <span className="reg-when">{a.studentName}</span>}
                                    </td>
                                    <td>{a.program}</td>
                                    <td>
                                        {a.yearLevelLabel}
                                        {/* What the student confirmed when they filed. Worth showing only
                                            when it disagrees with the record — that is the request worth
                                            a second look before approving. */}
                                        {a.declaredYearLevel && a.declaredYearLevel !== a.yearLevel ? (
                                            <span className="reg-when">
                                                Student confirmed {a.declaredYearLevelLabel}
                                            </span>
                                        ) : a.status === 'Pending' && (
                                            <span className="reg-when">Advances on approval</span>
                                        )}
                                    </td>
                                    <td>{a.semesterName}</td>
                                    <td className="reg-when">{formatPHT(a.requestedAtUtc)}</td>
                                    <td><span className={statusChip[a.status] || 'chip chip-muted'}>{a.status}</span></td>
                                    <td className="reg-actions">
                                        {a.status === 'Pending' && (
                                            <>
                                                <button className="btn btn-primary btn-sm" disabled={busyId === a.id}
                                                    onClick={() => act(a.id, true)}>
                                                    Approve
                                                </button>
                                                <button className="btn btn-ghost btn-sm" disabled={busyId === a.id}
                                                    onClick={() => act(a.id, false)}>
                                                    Reject
                                                </button>
                                            </>
                                        )}
                                    </td>
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

export default TermActivationsPage;
