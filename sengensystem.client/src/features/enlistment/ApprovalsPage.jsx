import { useEffect, useState } from 'react';
import { listApprovals, approveRequest, rejectRequest } from './api';
import { notifySuccess, notifyError } from '../shell/notify';
import { formatPHT } from '../registration/options';
import '../registration/registration.css';
import './enlistment.css';

/* FR-ENL-04: the Registrar's slot-approval queue. Approving consumes a seat (capacity is
   enforced transactionally server-side — 40 per section, FR-ENL-03) and emails the student;
   rejecting records an optional reason and emails it. */

const statusChip = {
    Requested: 'chip chip-yellow',
    Approved: 'chip chip-blue',
    Rejected: 'chip chip-muted',
    Cancelled: 'chip chip-muted'
};

function ApprovalsPage() {
    const [rows, setRows] = useState([]);
    const [pendingCount, setPendingCount] = useState(0);
    const [loading, setLoading] = useState(true);
    const [status, setStatus] = useState('Requested');
    const [search, setSearch] = useState('');
    const [appliedSearch, setAppliedSearch] = useState('');
    const [reload, setReload] = useState(0);
    const [busyId, setBusyId] = useState(null);
    const [rejecting, setRejecting] = useState(null); // { requestId, reason }
    const [alert, setAlert] = useState(null);

    useEffect(() => {
        let active = true;
        (async () => {
            setLoading(true);
            try {
                const data = await listApprovals({ status, search: appliedSearch });
                if (!active) return;
                setRows(data.requests);
                setPendingCount(data.pendingCount);
            } catch (err) {
                if (active) setAlert({ kind: 'error', text: err.message });
            } finally {
                if (active) setLoading(false);
            }
        })();
        return () => { active = false; };
    }, [status, appliedSearch, reload]);

    async function approve(row) {
        setBusyId(row.requestId);
        setAlert(null);
        try {
            const res = await approveRequest(row.requestId);
            const text = `Approved ${res.studentName} into ${res.subjectCode} (${res.sectionCode}) — ` +
                `${res.enrolled}/${res.capacity} seats now taken.`;
            setAlert({ kind: 'success', text });
            notifySuccess(text);
            setReload(v => v + 1);
        } catch (err) {
            setAlert({ kind: 'error', text: err.message });
            notifyError(err.message);
        } finally {
            setBusyId(null);
        }
    }

    async function reject() {
        if (!rejecting) return;
        setBusyId(rejecting.requestId);
        setAlert(null);
        try {
            const res = await rejectRequest(rejecting.requestId, rejecting.reason);
            setAlert({ kind: 'success', text: `Rejected the request of ${res.studentName} for ${res.subjectCode}.` });
            notifySuccess(`Rejected the request of ${res.studentName} for ${res.subjectCode}.`);
            setRejecting(null);
            setReload(v => v + 1);
        } catch (err) {
            setAlert({ kind: 'error', text: err.message });
            notifyError(err.message);
        } finally {
            setBusyId(null);
        }
    }

    return (
        <div className="reg-page">
            <header className="reg-head">
                <div>
                    <h2>Enlistment approvals</h2>
                    <p className="reg-sub">
                        Review student slot requests. Approving takes a seat (max 40 per section, enforced at
                        the database) and emails the student; rejections carry your note.
                    </p>
                </div>
                <div className="reg-controls">
                    <form onSubmit={e => { e.preventDefault(); setAppliedSearch(search); }} className="reg-search">
                        <input
                            type="search" placeholder="Search student or section…"
                            value={search} onChange={e => setSearch(e.target.value)}
                        />
                    </form>
                    <label className="reg-filter">
                        <span>Status</span>
                        <select value={status} onChange={e => setStatus(e.target.value)}>
                            {['Requested', 'Approved', 'Rejected', 'Cancelled', 'All'].map(s =>
                                <option key={s} value={s}>{s}</option>)}
                        </select>
                    </label>
                    <span className="chip chip-yellow">{pendingCount} pending</span>
                </div>
            </header>

            {alert && <div className={alert.kind === 'success' ? 'alert alert-success' : 'alert'}>{alert.text}</div>}

            {loading ? (
                <p className="reg-empty">Loading…</p>
            ) : rows.length === 0 ? (
                <p className="reg-empty">No slot requests in this view.</p>
            ) : (
                <div className="card reg-table-wrap">
                    <table className="reg-table">
                        <thead>
                            <tr>
                                <th>Student</th>
                                <th>Subject</th>
                                <th>Section</th>
                                <th>Seats</th>
                                <th>Requested</th>
                                <th>Status</th>
                                <th></th>
                            </tr>
                        </thead>
                        <tbody>
                            {rows.map(row => (
                                <tr key={row.requestId}>
                                    <td>
                                        <strong>{row.studentName}</strong>
                                        <span className="enl-subject-title reg-mono">{row.studentNumber}</span>
                                    </td>
                                    <td>
                                        <strong>{row.subjectCode}</strong>
                                        <span className="enl-subject-title">{row.subjectTitle}</span>
                                    </td>
                                    <td className="reg-mono">{row.sectionCode}</td>
                                    <td>
                                        <span className={row.enrolled >= row.capacity ? 'chip chip-yellow' : 'chip chip-muted'}>
                                            {row.enrolled}/{row.capacity}
                                        </span>
                                    </td>
                                    <td className="reg-when">{formatPHT(row.requestedAtUtc)}</td>
                                    <td>
                                        <span className={statusChip[row.status] || 'chip chip-muted'}>{row.status}</span>
                                        {row.rejectionReason && (
                                            <span className="enl-reject-reason" title={row.rejectionReason}> · {row.rejectionReason}</span>
                                        )}
                                    </td>
                                    <td>
                                        {row.status === 'Requested' && (
                                            rejecting?.requestId === row.requestId ? (
                                                <div className="enl-reject-form">
                                                    <input
                                                        type="text" placeholder="Reason (optional)"
                                                        value={rejecting.reason}
                                                        onChange={e => setRejecting({ ...rejecting, reason: e.target.value })}
                                                        autoFocus
                                                    />
                                                    <button className="btn btn-primary" type="button" disabled={busyId === row.requestId} onClick={reject}>
                                                        Confirm reject
                                                    </button>
                                                    <button className="btn" type="button" onClick={() => setRejecting(null)}>Back</button>
                                                </div>
                                            ) : (
                                                <div className="enl-actions">
                                                    <button
                                                        className="btn btn-primary" type="button"
                                                        disabled={busyId === row.requestId}
                                                        onClick={() => approve(row)}
                                                    >Approve</button>
                                                    <button
                                                        className="btn" type="button"
                                                        disabled={busyId === row.requestId}
                                                        onClick={() => setRejecting({ requestId: row.requestId, reason: '' })}
                                                    >Reject</button>
                                                </div>
                                            )
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

export default ApprovalsPage;
