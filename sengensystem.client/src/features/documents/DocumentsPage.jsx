import { useEffect, useState } from 'react';
import { useAuth } from '../auth/useAuth';
import { listChecklists, updateDocumentStatus, sendReminders, getMyLink, claimRecord } from './api';
import { notifySuccess, notifyError } from '../shell/notify';
import { documentStatusOptions, humanize } from '../registration/options';
import '../registration/registration.css';
import './documents.css';

/* FR-DOC: the digital admission-requirements checklist.
   Staff (Admission Officer / Registrar / School Admin) see the per-enrollee board and record
   each paper's submission status (FR-DOC-01..03) and trigger reminder emails (FR-DOC-05).
   Students see their own checklist — after claiming their SIS record, the identity link
   that subject enlistment requires (FR-ENL-05). */

const statusChipClass = {
    NotSubmitted: 'chip chip-muted',
    Submitted: 'chip chip-blue',
    XeroxCopy: 'chip chip-yellow'
};

// ---------- Student view ----------

function StudentChecklist() {
    const [state, setState] = useState(null); // { linked, claimable?, registration? }
    const [loading, setLoading] = useState(true);
    const [busy, setBusy] = useState(false);
    const [studentNumber, setStudentNumber] = useState('');
    const [dateOfBirth, setDateOfBirth] = useState('');
    const [alert, setAlert] = useState(null);

    useEffect(() => {
        let active = true;
        (async () => {
            try {
                const data = await getMyLink();
                if (active) setState(data);
            } catch (err) {
                if (active) setAlert({ kind: 'error', text: err.message });
            } finally {
                if (active) setLoading(false);
            }
        })();
        return () => { active = false; };
    }, []);

    async function claim(e) {
        e.preventDefault();
        setBusy(true);
        setAlert(null);
        try {
            const data = await claimRecord(studentNumber.trim(), dateOfBirth);
            setState(data);
            setAlert({ kind: 'success', text: `Linked! Welcome, ${data.registration.fullName}.` });
            notifySuccess(`Student record ${data.registration.studentNumber} linked to your account.`);
        } catch (err) {
            const text = err.fieldErrors?.studentNumber?.[0] || err.fieldErrors?.dateOfBirth?.[0] || err.message;
            setAlert({ kind: 'error', text });
            notifyError(text);
        } finally {
            setBusy(false);
        }
    }

    if (loading) return <p className="reg-empty">Loading your record…</p>;

    if (!state?.linked) {
        return (
            <div className="doc-claim card">
                <h3>Claim your student record</h3>
                <p>
                    Enter the student number from your SIS registration confirmation email and your date
                    of birth to link your record. Your account email must match the email on your SIS
                    {state?.claimable && <> — <strong>we found a record matching your email, so you're one step away</strong></>}.
                </p>
                {alert && <div className={alert.kind === 'success' ? 'alert alert-success' : 'alert'}>{alert.text}</div>}
                <form onSubmit={claim} className="doc-claim-form">
                    <input
                        type="text"
                        placeholder="e.g. 2026-000001"
                        value={studentNumber}
                        onChange={e => setStudentNumber(e.target.value)}
                        required
                    />
                    <input
                        type="date"
                        aria-label="Date of birth"
                        value={dateOfBirth}
                        onChange={e => setDateOfBirth(e.target.value)}
                        required
                    />
                    <button className="btn btn-primary" type="submit" disabled={busy}>
                        {busy ? 'Linking…' : 'Link my record'}
                    </button>
                </form>
            </div>
        );
    }

    const r = state.registration;
    return (
        <div className="doc-student">
            {alert && <div className={alert.kind === 'success' ? 'alert alert-success' : 'alert'}>{alert.text}</div>}
            <div className="doc-student-summary card">
                <div>
                    <h3>{r.fullName}</h3>
                    <span className="reg-mono">{r.studentNumber}</span> · {r.program} · {humanize(r.studentType)}
                </div>
                <div className="doc-student-chips">
                    <span className={r.registrationStatus === 'Confirmed' ? 'chip chip-blue' : 'chip chip-muted'}>
                        SIS {r.registrationStatus}
                    </span>
                    <span className={r.documentsComplete ? 'chip chip-blue' : 'chip chip-yellow'}>
                        Requirements {r.submittedCount}/{r.totalCount}
                    </span>
                    <span className={r.isPreAuthorized ? 'chip chip-blue' : 'chip chip-muted'}>
                        {r.isPreAuthorized ? 'Cleared for enlistment' : 'Not yet cleared for enlistment'}
                    </span>
                </div>
            </div>
            <div className="card doc-list-card">
                <h4>Admission requirements</h4>
                <ul className="doc-list">
                    {r.documents.map(d => (
                        <li key={d.documentType}>
                            <span>{d.label}</span>
                            <span className={statusChipClass[d.status] || 'chip chip-muted'}>
                                {humanize(d.status)}
                            </span>
                        </li>
                    ))}
                </ul>
                {!r.documentsComplete && (
                    <p className="doc-hint">
                        Bring the missing papers to the Admission Office — a complete checklist is required
                        before you can be cleared for subject enlistment.
                    </p>
                )}
            </div>
        </div>
    );
}

// ---------- Staff view ----------

function StaffBoard() {
    const [rows, setRows] = useState([]);
    const [completeCount, setCompleteCount] = useState(0);
    const [loading, setLoading] = useState(true);
    const [completion, setCompletion] = useState('All');
    const [search, setSearch] = useState('');
    const [appliedSearch, setAppliedSearch] = useState('');
    const [reload, setReload] = useState(0);
    const [expanded, setExpanded] = useState(null); // registrationId
    const [alert, setAlert] = useState(null);
    const [remindBusy, setRemindBusy] = useState(false);

    useEffect(() => {
        let active = true;
        (async () => {
            setLoading(true);
            try {
                const data = await listChecklists({ completion, search: appliedSearch });
                if (!active) return;
                setRows(data.checklists);
                setCompleteCount(data.completeCount);
            } catch (err) {
                if (active) setAlert({ kind: 'error', text: err.message });
            } finally {
                if (active) setLoading(false);
            }
        })();
        return () => { active = false; };
    }, [completion, appliedSearch, reload]);

    async function setDocStatus(row, doc, status) {
        setAlert(null);
        try {
            const result = await updateDocumentStatus(doc.id, status);
            notifySuccess(
                `${doc.label} of ${row.studentNumber} set to ${humanize(status)} ` +
                `(${result.submittedCount}/${result.totalCount}).`);
            setReload(v => v + 1);
        } catch (err) {
            setAlert({ kind: 'error', text: err.message });
            notifyError(err.message);
        }
    }

    async function remind(registrationId) {
        setRemindBusy(true);
        setAlert(null);
        try {
            const result = await sendReminders(registrationId);
            const text = `Reminder emails sent to ${result.emailsSent} of ${result.targeted} enrollee(s) with incomplete checklists.`;
            setAlert({ kind: 'success', text });
            notifySuccess(text);
        } catch (err) {
            setAlert({ kind: 'error', text: err.message });
            notifyError(err.message);
        } finally {
            setRemindBusy(false);
        }
    }

    return (
        <>
            <header className="reg-head">
                <div>
                    <h2>Document requirements</h2>
                    <p className="reg-sub">
                        Per-enrollee admission checklist — record each paper as it arrives, track completion,
                        and remind students with missing requirements.
                    </p>
                </div>
                <div className="reg-controls">
                    <form onSubmit={e => { e.preventDefault(); setAppliedSearch(search); }} className="reg-search">
                        <input
                            type="search" placeholder="Search name or student no."
                            value={search} onChange={e => setSearch(e.target.value)}
                        />
                    </form>
                    <label className="reg-filter">
                        <span>Checklist</span>
                        <select value={completion} onChange={e => setCompletion(e.target.value)}>
                            {['All', 'Complete', 'Incomplete'].map(s => <option key={s} value={s}>{s}</option>)}
                        </select>
                    </label>
                    <button
                        className="btn btn-primary" type="button" disabled={remindBusy}
                        onClick={() => remind(null)}
                        title="Email every enrollee whose checklist is incomplete"
                    >
                        {remindBusy ? 'Sending…' : 'Send reminders'}
                    </button>
                </div>
            </header>

            {alert && <div className={alert.kind === 'success' ? 'alert alert-success' : 'alert'}>{alert.text}</div>}

            {loading ? (
                <p className="reg-empty">Loading…</p>
            ) : rows.length === 0 ? (
                <p className="reg-empty">No enrollees found.</p>
            ) : (
                <div className="card reg-table-wrap">
                    <table className="reg-table">
                        <thead>
                            <tr>
                                <th>Student no.</th>
                                <th>Name</th>
                                <th>Program</th>
                                <th>Type</th>
                                <th>Requirements</th>
                                <th>SIS status</th>
                                <th>Checklist</th>
                            </tr>
                        </thead>
                        <tbody>
                            {rows.map(row => (
                                <>
                                    <tr
                                        key={row.registrationId}
                                        className="reg-row"
                                        onClick={() => setExpanded(expanded === row.registrationId ? null : row.registrationId)}
                                    >
                                        <td className="reg-mono">{row.studentNumber}</td>
                                        <td><strong>{row.fullName}</strong></td>
                                        <td>{row.program}</td>
                                        <td>{humanize(row.studentType)}</td>
                                        <td>{row.submittedCount}/{row.totalCount}</td>
                                        <td>
                                            <span className={row.registrationStatus === 'Confirmed' ? 'chip chip-blue' : 'chip chip-muted'}>
                                                {row.registrationStatus}
                                            </span>
                                        </td>
                                        <td>
                                            <span className={row.isComplete ? 'chip chip-blue' : 'chip chip-yellow'}>
                                                {row.isComplete ? 'Complete' : 'Incomplete'}
                                            </span>
                                        </td>
                                    </tr>
                                    {expanded === row.registrationId && (
                                        <tr key={`${row.registrationId}-docs`} className="doc-expand-row">
                                            <td colSpan={7}>
                                                <div className="doc-grid">
                                                    {row.documents.map(doc => (
                                                        <label className="doc-grid-item" key={doc.id} onClick={e => e.stopPropagation()}>
                                                            <span>{doc.label}</span>
                                                            <select
                                                                value={doc.status}
                                                                onChange={e => setDocStatus(row, doc, e.target.value)}
                                                            >
                                                                {documentStatusOptions.map(o => (
                                                                    <option key={o.value} value={o.value}>{o.label}</option>
                                                                ))}
                                                            </select>
                                                        </label>
                                                    ))}
                                                </div>
                                                {!row.isComplete && (
                                                    <button
                                                        className="btn doc-remind-one" type="button" disabled={remindBusy}
                                                        onClick={e => { e.stopPropagation(); remind(row.registrationId); }}
                                                    >
                                                        Email this student a reminder
                                                    </button>
                                                )}
                                            </td>
                                        </tr>
                                    )}
                                </>
                            ))}
                        </tbody>
                    </table>
                </div>
            )}
            <p className="doc-footnote">
                {completeCount} of {rows.length} listed checklists are complete. A checklist is complete when
                every paper is received as an original or photocopy.
            </p>
        </>
    );
}

function DocumentsPage() {
    const { user } = useAuth();
    return (
        <div className="reg-page">
            {user.role === 'Student' ? (
                <>
                    <header className="reg-head">
                        <div>
                            <h2>Document requirements</h2>
                            <p className="reg-sub">
                                Your admission checklist and its submission status, per your SIS registration.
                            </p>
                        </div>
                    </header>
                    <StudentChecklist />
                </>
            ) : (
                <StaffBoard />
            )}
        </div>
    );
}

export default DocumentsPage;
