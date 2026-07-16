import { useEffect, useMemo, useState } from 'react';
import { browseSections, requestSlot, myEnlistment, cancelRequest } from './api';
import { notifySuccess, notifyError } from '../shell/notify';
import { formatPHT } from '../registration/options';
import './enlistment.css';

/* FR-ENL: the student's subject enlistment. Browse published sections with live seat
   availability (FR-ENL-01/02), request seats routed through Registrar approval (FR-ENL-04),
   and track/cancel pending requests. Requests are refused server-side when ineligible
   (FR-ENL-05), the section is full (FR-ENL-03), or times overlap (FR-ENL-07). */

const requestChip = {
    Requested: 'chip chip-yellow',
    Approved: 'chip chip-blue',
    Rejected: 'chip chip-muted',
    Cancelled: 'chip chip-muted'
};

function availabilityClass(available, capacity) {
    if (available <= 0) return 'enl-avail enl-avail-full';
    if (available <= capacity * 0.2) return 'enl-avail enl-avail-low';
    return 'enl-avail';
}

function EnlistmentPage() {
    const [data, setData] = useState(null);       // browse payload
    const [mine, setMine] = useState(null);       // my requests payload
    const [loading, setLoading] = useState(true);
    const [busyId, setBusyId] = useState(null);
    const [alert, setAlert] = useState(null);
    const [search, setSearch] = useState('');

    async function load() {
        try {
            const [browse, my] = await Promise.all([browseSections(), myEnlistment()]);
            setData(browse);
            setMine(my);
        } catch (err) {
            setAlert({ kind: 'error', text: err.message, reasons: err.reasons });
        } finally {
            setLoading(false);
        }
    }

    useEffect(() => {
        const initial = setTimeout(load, 0);
        return () => clearTimeout(initial);
    }, []);

    const sections = useMemo(() => {
        if (!data) return [];
        const term = search.trim().toLowerCase();
        if (!term) return data.sections;
        return data.sections.filter(s =>
            s.subjectCode.toLowerCase().includes(term)
            || s.subjectTitle.toLowerCase().includes(term)
            || s.sectionCode.toLowerCase().includes(term));
    }, [data, search]);

    async function request(section) {
        setBusyId(section.sectionId);
        setAlert(null);
        try {
            const res = await requestSlot(section.sectionId);
            setAlert({ kind: 'success', text: res.message });
            notifySuccess(res.message);
            await load();
        } catch (err) {
            setAlert({ kind: 'error', text: err.message, reasons: err.reasons });
            notifyError(err.message);
        } finally {
            setBusyId(null);
        }
    }

    async function cancel(row) {
        setBusyId(row.requestId);
        setAlert(null);
        try {
            await cancelRequest(row.requestId);
            setAlert({ kind: 'success', text: `Cancelled your request for ${row.subjectCode} (${row.sectionCode}).` });
            notifySuccess(`Cancelled your request for ${row.subjectCode} (${row.sectionCode}).`);
            await load();
        } catch (err) {
            setAlert({ kind: 'error', text: err.message });
            notifyError(err.message);
        } finally {
            setBusyId(null);
        }
    }

    if (loading) return <div className="enl-page"><p className="enl-empty">Loading published sections…</p></div>;

    const eligibility = data?.eligibility;

    return (
        <div className="enl-page">
            <header className="enl-head">
                <div>
                    <h2>Subject enlistment</h2>
                    <p className="enl-sub">
                        Published sections for
                        {data?.semesterName ? <> <strong>{data.semesterName}</strong></> : ' the active semester'} with
                        live seat availability. Seats are confirmed once the Registrar approves your request.
                    </p>
                </div>
                <div className="enl-controls">
                    <input
                        type="search" placeholder="Search subject or section…"
                        value={search} onChange={e => setSearch(e.target.value)}
                    />
                    <button className="btn" type="button" onClick={load}>Refresh</button>
                </div>
            </header>

            {eligibility && !eligibility.eligible && (
                <div className="alert enl-gate">
                    <p><strong>You are not yet cleared to enlist.</strong></p>
                    <ul>
                        {eligibility.blockers.map((b, i) => <li key={i}>{b}</li>)}
                    </ul>
                </div>
            )}

            {alert && (
                <div className={alert.kind === 'success' ? 'alert alert-success' : 'alert'}>
                    <p>{alert.text}</p>
                    {alert.reasons?.length > 0 && (
                        <ul>{alert.reasons.map((r, i) => <li key={i}>{r}</li>)}</ul>
                    )}
                </div>
            )}

            {mine && mine.count > 0 && (
                <section className="card enl-mine">
                    <header className="enl-mine-head">
                        <h3>My requests</h3>
                        <span className="chip chip-blue">{mine.approvedUnits} units approved</span>
                    </header>
                    <div className="enl-table-wrap">
                        <table className="enl-table">
                            <thead>
                                <tr>
                                    <th>Subject</th>
                                    <th>Section</th>
                                    <th>Units</th>
                                    <th>Requested</th>
                                    <th>Status</th>
                                    <th></th>
                                </tr>
                            </thead>
                            <tbody>
                                {mine.requests.map(row => (
                                    <tr key={row.requestId}>
                                        <td>
                                            <strong>{row.subjectCode}</strong>
                                            <span className="enl-subject-title">{row.subjectTitle}</span>
                                        </td>
                                        <td className="enl-mono">{row.sectionCode}</td>
                                        <td>{row.units}</td>
                                        <td className="enl-when">{formatPHT(row.requestedAtUtc)}</td>
                                        <td>
                                            <span className={requestChip[row.status] || 'chip chip-muted'}>{row.status}</span>
                                            {row.rejectionReason && (
                                                <span className="enl-reject-reason" title={row.rejectionReason}> · {row.rejectionReason}</span>
                                            )}
                                        </td>
                                        <td>
                                            {row.status === 'Requested' && (
                                                <button
                                                    className="btn enl-cancel" type="button"
                                                    disabled={busyId === row.requestId}
                                                    onClick={() => cancel(row)}
                                                >Cancel</button>
                                            )}
                                        </td>
                                    </tr>
                                ))}
                            </tbody>
                        </table>
                    </div>
                </section>
            )}

            {sections.length === 0 ? (
                <p className="enl-empty">
                    {data?.count === 0
                        ? 'No published sections yet — schedules appear here once the Registrar publishes them.'
                        : 'No sections match your search.'}
                </p>
            ) : (
                <div className="enl-grid">
                    {sections.map(s => (
                        <article className="card enl-card" key={s.sectionId}>
                            <header className="enl-card-head">
                                <div>
                                    <h3>{s.subjectCode}</h3>
                                    <p className="enl-subject-title">{s.subjectTitle}</p>
                                </div>
                                <span className={availabilityClass(s.available, s.capacity)}>
                                    {s.available > 0 ? `${s.available} of ${s.capacity} slots left` : 'Full'}
                                </span>
                            </header>
                            <p className="enl-card-meta">
                                <span className="enl-mono">{s.sectionCode}</span> · Block {s.cohortKey} · {s.units} units
                            </p>
                            <ul className="enl-meetings">
                                {s.meetings.map((m, i) => (
                                    <li key={i}>
                                        <span className="enl-mono">{m.day.slice(0, 3)} {m.time}</span>
                                        <span>{m.room}</span>
                                        <span className="enl-faculty">{m.faculty}</span>
                                    </li>
                                ))}
                            </ul>
                            <footer className="enl-card-foot">
                                {s.myStatus ? (
                                    <span className={requestChip[s.myStatus] || 'chip chip-muted'}>
                                        {s.myStatus === 'Requested' ? 'Awaiting approval' : s.myStatus}
                                    </span>
                                ) : (
                                    <button
                                        className="btn btn-primary" type="button"
                                        disabled={busyId === s.sectionId || s.available <= 0 || !eligibility?.eligible}
                                        title={!eligibility?.eligible
                                            ? 'Complete your requirements and pre-authorization first'
                                            : s.available <= 0 ? 'This section is full' : undefined}
                                        onClick={() => request(s)}
                                    >
                                        {busyId === s.sectionId ? 'Requesting…' : 'Request seat'}
                                    </button>
                                )}
                            </footer>
                        </article>
                    ))}
                </div>
            )}
        </div>
    );
}

export default EnlistmentPage;
