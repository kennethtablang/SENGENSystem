import { useEffect, useState } from 'react';
import { listTermActivations, validateTermActivation } from './api';
import { notifySuccess, notifyError } from '../shell/notify';
import { formatPHT } from './options';
import './registration.css';

const statusChip = {
    Pending: 'chip chip-muted',
    Validated: 'chip chip-blue',
    Rejected: 'chip chip-yellow'
};

function TermActivationsPage() {
    const [activations, setActivations] = useState([]);
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState(null);
    const [filter, setFilter] = useState('Pending');
    const [busyId, setBusyId] = useState(null);
    const [reload, setReload] = useState(0);

    useEffect(() => {
        let active = true;
        (async () => {
            setLoading(true);
            setError(null);
            try {
                const data = await listTermActivations(filter);
                if (active) setActivations(data.activations);
            } catch (err) {
                if (active) setError(err.message);
            } finally {
                if (active) setLoading(false);
            }
        })();
        return () => { active = false; };
    }, [filter, reload]);

    async function act(id, approve) {
        setBusyId(id);
        setError(null);
        try {
            const remarks = approve ? null : (window.prompt('Reason for rejection (optional):') ?? '');
            await validateTermActivation(id, { approve, remarks });
            notifySuccess(approve ? 'Term activation approved.' : 'Term activation rejected.');
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
                        emails the student a confirmation.
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
                                <th>Student no.</th>
                                <th>Name</th>
                                <th>Program</th>
                                <th>Term</th>
                                <th>Requested</th>
                                <th>Status</th>
                                <th></th>
                            </tr>
                        </thead>
                        <tbody>
                            {activations.map(a => (
                                <tr key={a.id}>
                                    <td className="reg-mono">{a.studentNumber}</td>
                                    <td><strong>{a.studentName}</strong></td>
                                    <td>{a.program}</td>
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
                </div>
            )}
        </div>
    );
}

export default TermActivationsPage;
