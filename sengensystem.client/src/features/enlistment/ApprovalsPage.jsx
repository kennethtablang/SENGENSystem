import { useEffect, useMemo, useState } from 'react';
import { listApprovals, approveRequest, bulkApprove, rejectRequest, overrideCapacity } from './api';
import { notifySuccess, notifyError } from '../shell/notify';
import { confirmAction } from '../shell/confirm';
import { confirmsHeavy } from '../settings/prefs';
import { formatPHT } from '../registration/options';
import '../registration/registration.css';
import './enlistment.css';

/* FR-ENL-04: the Registrar's slot-approval queue. Approving consumes a seat (capacity is
   enforced transactionally server-side — 40 per section, FR-ENL-03) and emails the student;
   rejecting records an optional reason and emails it.

   FR-ENL-08: the queue runs hundreds of rows deep in the first days of enlistment, and clicking
   Approve once per row is the bottleneck the Registrar actually feels. Rows are selectable, a whole
   student's load can be taken in one action, and the queue can be swept — but every request in a
   batch still passes the same per-request checks, and anything skipped comes back with its reason
   rather than failing the run. */

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
    const [capEditing, setCapEditing] = useState(null); // { sectionId, sectionCode, capacity, reason }
    const [capBusy, setCapBusy] = useState(false);
    const [alert, setAlert] = useState(null);
    // Checked request ids, and the in-flight flag for a batch.
    const [selected, setSelected] = useState(() => new Set());
    const [bulkBusy, setBulkBusy] = useState(false);

    // Only pending rows can be decided, so only they are selectable.
    const pendingRows = useMemo(() => rows.filter(r => r.status === 'Requested'), [rows]);
    const selectedPending = useMemo(
        () => pendingRows.filter(r => selected.has(r.requestId)), [pendingRows, selected]);
    const allSelected = pendingRows.length > 0 && selectedPending.length === pendingRows.length;

    function toggleRow(requestId) {
        setSelected(prev => {
            const next = new Set(prev);
            if (next.has(requestId)) next.delete(requestId); else next.add(requestId);
            return next;
        });
    }

    function toggleAll() {
        setSelected(allSelected ? new Set() : new Set(pendingRows.map(r => r.requestId)));
    }

    /** Runs a batch and reports it honestly: how many went through, and why the rest didn't. */
    async function runBulk(payload, describe) {
        setBulkBusy(true);
        setAlert(null);
        try {
            const result = await bulkApprove(payload);
            const skipped = result.outcomes.filter(o => !o.approved);
            const text = result.approvedCount === 0 && skipped.length === 0
                ? 'There was nothing pending to approve.'
                : `Approved ${result.approvedCount} request${result.approvedCount === 1 ? '' : 's'}`
                    + (skipped.length > 0 ? ` · ${skipped.length} skipped.` : '.');
            setAlert({
                kind: skipped.length > 0 && result.approvedCount === 0 ? 'error' : 'success',
                text,
                reasons: skipped.map(o =>
                    `${o.studentNumber} · ${o.subjectCode} (${o.sectionCode}): ${o.reason}`)
            });
            if (result.approvedCount > 0) notifySuccess(text);
            setSelected(new Set());
            setReload(v => v + 1);
        } catch (err) {
            setAlert({ kind: 'error', text: err.message });
            notifyError(err.message);
        } finally {
            setBulkBusy(false);
        }
        return describe;
    }

    async function approveSelected() {
        if (selectedPending.length === 0) return;
        const ok = !confirmsHeavy() || await confirmAction({
            title: `Approve ${selectedPending.length} request${selectedPending.length === 1 ? '' : 's'}?`,
            message: 'Each one takes a seat in its section and emails the student. Requests that would '
                + 'clash with the student’s other classes, or that hit a full section, are skipped and '
                + 'reported back — the rest go through.',
            confirmLabel: `Approve ${selectedPending.length}`
        });
        if (!ok) return;
        await runBulk({ requestIds: selectedPending.map(r => r.requestId) });
    }

    async function approveAllPending() {
        const ok = await confirmAction({
            title: `Approve all ${pendingCount} pending request${pendingCount === 1 ? '' : 's'}?`,
            message: 'This sweeps the whole queue for the active term, oldest request first so the '
                + 'students who queued earliest get the seats. Clashes and full sections are skipped '
                + 'and reported; every approval emails its student and cannot be recalled.',
            confirmLabel: `Approve all ${pendingCount}`
        });
        if (!ok) return;
        await runBulk({ allPending: true });
    }

    /** A student's whole outstanding load in one action — the common case at the window. */
    async function approveStudent(row) {
        const theirs = pendingRows.filter(r => r.studentNumber === row.studentNumber);
        const ok = !confirmsHeavy() || await confirmAction({
            title: `Approve ${row.studentName}'s ${theirs.length} pending request${theirs.length === 1 ? '' : 's'}?`,
            message: 'Approves every subject this student is still waiting on. Anything that clashes '
                + 'with their other classes, or hits a full section, is skipped and reported.',
            confirmLabel: `Approve ${theirs.length}`
        });
        if (!ok) return;
        await runBulk({ requestIds: theirs.map(r => r.requestId) });
    }

    useEffect(() => {
        let active = true;
        (async () => {
            setLoading(true);
            try {
                const data = await listApprovals({ status, search: appliedSearch });
                if (!active) return;
                setRows(data.requests);
                setPendingCount(data.pendingCount);
                // A fresh page of results invalidates a selection made against the old one.
                setSelected(new Set());
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

    async function saveCap() {
        if (!capEditing) return;
        const capacity = Number(capEditing.capacity);
        if (!Number.isInteger(capacity) || capacity < 1) {
            setAlert({ kind: 'error', text: 'Enter a whole number of seats (1 or more).' });
            return;
        }
        setCapBusy(true);
        setAlert(null);
        try {
            const res = await overrideCapacity(capEditing.sectionId, capacity, capEditing.reason);
            const text = `Raised ${res.sectionCode} to ${res.capacity} seats (${res.enrolled} taken).`;
            setAlert({ kind: 'success', text });
            notifySuccess(text);
            setCapEditing(null);
            setReload(v => v + 1);
        } catch (err) {
            setAlert({ kind: 'error', text: err.message });
            notifyError(err.message);
        } finally {
            setCapBusy(false);
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
                    <button
                        className="btn btn-primary" type="button"
                        disabled={bulkBusy || pendingCount === 0}
                        title="Approve every pending request in the active term, oldest first"
                        onClick={approveAllPending}
                    >
                        {bulkBusy ? 'Approving…' : `Approve all ${pendingCount}`}
                    </button>
                </div>
            </header>

            {selectedPending.length > 0 && (
                <div className="enl-bulkbar">
                    <span>
                        <strong>{selectedPending.length}</strong> request{selectedPending.length === 1 ? '' : 's'} selected
                    </span>
                    <button className="btn btn-sm btn-ghost" type="button" onClick={() => setSelected(new Set())}>
                        Clear
                    </button>
                    <button
                        className="btn btn-sm btn-primary" type="button"
                        disabled={bulkBusy} onClick={approveSelected}
                    >
                        {bulkBusy ? 'Approving…' : `Approve ${selectedPending.length}`}
                    </button>
                </div>
            )}

            {alert && (
                <div className={alert.kind === 'success' ? 'alert alert-success' : 'alert'}>
                    <p>{alert.text}</p>
                    {alert.reasons?.length > 0 && (
                        <ul className="enl-skipped">
                            {alert.reasons.map((r, i) => <li key={i}>{r}</li>)}
                        </ul>
                    )}
                </div>
            )}

            {loading ? (
                <p className="reg-empty">Loading…</p>
            ) : rows.length === 0 ? (
                <p className="reg-empty">No slot requests in this view.</p>
            ) : (
                <div className="card reg-table-wrap">
                    <table className="reg-table">
                        <thead>
                            <tr>
                                <th className="enl-check-col">
                                    <input
                                        type="checkbox"
                                        aria-label="Select every pending request on this page"
                                        checked={allSelected}
                                        disabled={pendingRows.length === 0 || bulkBusy}
                                        onChange={toggleAll}
                                    />
                                </th>
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
                                <tr key={row.requestId} className={selected.has(row.requestId) ? 'is-selected' : undefined}>
                                    <td className="enl-check-col">
                                        {row.status === 'Requested' && (
                                            <input
                                                type="checkbox"
                                                aria-label={`Select ${row.studentName}'s request for ${row.subjectCode}`}
                                                checked={selected.has(row.requestId)}
                                                disabled={bulkBusy}
                                                onChange={() => toggleRow(row.requestId)}
                                            />
                                        )}
                                    </td>
                                    <td>
                                        <strong>{row.studentName}</strong>
                                        <span className="enl-subject-title reg-mono">{row.studentNumber}</span>
                                        {row.status === 'Requested'
                                            && pendingRows.filter(r => r.studentNumber === row.studentNumber).length > 1 && (
                                            <button
                                                className="link-btn" type="button"
                                                disabled={bulkBusy}
                                                title="Approve every subject this student is still waiting on"
                                                onClick={() => approveStudent(row)}
                                            >
                                                Approve all {pendingRows.filter(r => r.studentNumber === row.studentNumber).length}
                                            </button>
                                        )}
                                    </td>
                                    <td>
                                        <strong>{row.subjectCode}</strong>
                                        <span className="enl-subject-title">{row.subjectTitle}</span>
                                    </td>
                                    <td className="reg-mono">{row.sectionCode}</td>
                                    <td>
                                        {capEditing?.sectionId === row.sectionId ? (
                                            <div className="enl-cap-form">
                                                <input
                                                    type="number" min="1" max="200" step="1"
                                                    aria-label="New seat capacity"
                                                    value={capEditing.capacity}
                                                    onChange={e => setCapEditing({ ...capEditing, capacity: e.target.value })}
                                                    autoFocus
                                                />
                                                <input
                                                    type="text" placeholder="Reason (optional)"
                                                    value={capEditing.reason}
                                                    onChange={e => setCapEditing({ ...capEditing, reason: e.target.value })}
                                                />
                                                <button className="btn btn-primary" type="button" disabled={capBusy} onClick={saveCap}>
                                                    Save cap
                                                </button>
                                                <button className="btn" type="button" onClick={() => setCapEditing(null)}>Back</button>
                                            </div>
                                        ) : (
                                            <div className="enl-cap-cell">
                                                <span className={row.enrolled >= row.capacity ? 'chip chip-yellow' : 'chip chip-muted'}>
                                                    {row.enrolled}/{row.capacity}
                                                </span>
                                                <button
                                                    className="link-btn" type="button"
                                                    title="Raise this section's seat cap (FR-ENL-03)"
                                                    onClick={() => setCapEditing({
                                                        sectionId: row.sectionId,
                                                        sectionCode: row.sectionCode,
                                                        capacity: row.capacity + 1,
                                                        reason: ''
                                                    })}
                                                >Raise cap</button>
                                            </div>
                                        )}
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
