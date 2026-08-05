import { useEffect, useMemo, useState } from 'react';
import { browseSections, requestSlot, myEnlistment, cancelRequest } from './api';
import { notifySuccess, notifyError } from '../shell/notify';
import { confirmAction } from '../shell/confirm';
import { hhmm } from '../scheduling/calendarUtils';
import { formatPHT } from '../registration/options';
import { useTableControls } from '../shell/useTableControls';
import { SortHeader, Pagination } from '../shell/tableControls';
import './enlistment.css';

/* FR-ENL: the student's subject enlistment. Browse published sections with live seat
   availability (FR-ENL-01/02), request seats routed through Registrar approval (FR-ENL-04),
   and track/cancel pending requests. Requests are refused server-side when ineligible
   (FR-ENL-05), the section is full (FR-ENL-03), or times overlap (FR-ENL-07).

   The page opens on the student's own curriculum, not the institution's catalog: the checklist
   at the top is the subjects their program and year level still owe this term, and the section
   cards below are narrowed to exactly those (FR-ENL-01/06). "Show every section" widens the
   browse only — the server re-checks the same list when a seat is actually requested. */

const requestChip = {
    Requested: 'chip chip-yellow',
    Approved: 'chip chip-blue',
    Rejected: 'chip chip-muted',
    Cancelled: 'chip chip-muted',
    Dropped: 'chip chip-muted'
};

/* Where each subject on the checklist stands. NoSection is the one the student can do nothing
   about — say so plainly rather than leaving them hunting for a card that isn't there. */
const planStatus = {
    Approved: { chip: 'chip chip-blue', label: 'Enrolled' },
    Requested: { chip: 'chip chip-yellow', label: 'Awaiting approval' },
    Open: { chip: 'chip chip-muted', label: 'Not yet requested' },
    NoSection: { chip: 'chip chip-muted', label: 'No class published yet' }
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
    const [showAll, setShowAll] = useState(false);

    async function load(all = showAll) {
        try {
            const [browse, my] = await Promise.all([browseSections({ all }), myEnlistment()]);
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

    async function toggleShowAll() {
        const next = !showAll;
        setShowAll(next);
        setLoading(true);
        await load(next);
    }

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

    /* Withdraw from a section. One call covers both cases — a request still awaiting approval is
       cancelled, an approved one is dropped and its seat goes back to the section — but they are
       not the same act. Giving up a class you already hold asks first, because the seat may be the
       last one and someone else can take it the moment it is free. */
    async function withdraw(row) {
        const wasApproved = row.status === 'Approved';
        if (wasApproved && !(await confirmAction({
            title: `Drop ${row.subjectCode}?`,
            message: `Your seat in ${row.sectionCode} goes straight back to the section, and someone `
                + 'else may take it. You can request it again while enlistment is open — if the '
                + 'section fills up first, you would have to pick another one.',
            confirmLabel: 'Drop the class',
            danger: true
        }))) return;

        setBusyId(row.requestId);
        setAlert(null);
        try {
            await cancelRequest(row.requestId);
            const text = wasApproved
                ? `Dropped ${row.subjectCode} (${row.sectionCode}). The seat is back in the section.`
                : `Cancelled your request for ${row.subjectCode} (${row.sectionCode}).`;
            setAlert({ kind: 'success', text });
            notifySuccess(text);
            await load();
        } catch (err) {
            setAlert({ kind: 'error', text: err.message });
            notifyError(err.message);
        } finally {
            setBusyId(null);
        }
    }

    // Both student tables are short — a term's subjects, a term's requests — so they sort but keep
    // a page size well above what either ever holds. Declared before the loading return so the
    // hooks stay unconditional.
    const planTable = useTableControls(data?.plan?.subjects, {
        columns: {
            subjectCode: s => s.subjectCode,
            units: s => s.units,
            sectionCount: s => s.sectionCount,
            status: s => s.status
        },
        initialSort: { key: 'subjectCode', dir: 'asc' },
        initialPageSize: 50
    });

    const mineTable = useTableControls(mine?.requests, {
        columns: {
            subjectCode: r => r.subjectCode,
            sectionCode: r => r.sectionCode,
            units: r => r.units,
            requestedAtUtc: r => r.requestedAtUtc,
            status: r => r.status
        },
        initialSort: { key: 'requestedAtUtc', dir: 'desc' },
        initialPageSize: 50
    });

    if (loading) return <div className="enl-page"><p className="enl-empty">Loading published sections…</p></div>;

    const eligibility = data?.eligibility;
    const plan = data?.plan;
    const hasPlan = plan?.resolved && plan.subjects.length > 0;

    return (
        <div className="enl-page">
            <header className="enl-head">
                <div>
                    <h2>Subject enlistment</h2>
                    <p className="enl-sub">
                        {plan?.filtered ? (
                            <>
                                Your <strong>{plan.programCode} {plan.yearLevelLabel}</strong> subjects for
                                {data?.semesterName ? <> <strong>{data.semesterName}</strong></> : ' the active semester'},
                                with live seat availability. Seats are confirmed once the Registrar approves
                                your request.
                            </>
                        ) : (
                            <>
                                Published sections for
                                {data?.semesterName ? <> <strong>{data.semesterName}</strong></> : ' the active semester'} with
                                live seat availability. Seats are confirmed once the Registrar approves your request.
                            </>
                        )}
                    </p>
                </div>
                <div className="enl-controls">
                    <input
                        type="search" placeholder="Search subject or section…"
                        value={search} onChange={e => setSearch(e.target.value)}
                    />
                    <button className="btn" type="button" onClick={() => load()}>Refresh</button>
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

            {plan?.notice && <div className="alert">{plan.notice}</div>}

            {hasPlan && (
                <section className="card enl-plan">
                    <header className="enl-plan-head">
                        <div>
                            <h3>Subjects you need this term</h3>
                            <p className="enl-plan-sub">
                                {plan.programName || plan.programCode} · {plan.yearLevelLabel} · {plan.termLabel}
                            </p>
                        </div>
                        <span className="chip chip-blue">
                            {plan.subjectCount} {plan.subjectCount === 1 ? 'subject' : 'subjects'} · {plan.units} units
                        </span>
                    </header>
                    <div className="enl-table-wrap">
                        <table className="enl-table">
                            <thead>
                                <tr>
                                    <SortHeader label="Subject" sortKey="subjectCode" sort={planTable.sort} onSort={planTable.toggleSort} />
                                    <SortHeader label="Units" sortKey="units" sort={planTable.sort} onSort={planTable.toggleSort} />
                                    <SortHeader label="Classes" sortKey="sectionCount" sort={planTable.sort} onSort={planTable.toggleSort} />
                                    <SortHeader label="Status" sortKey="status" sort={planTable.sort} onSort={planTable.toggleSort} />
                                </tr>
                            </thead>
                            <tbody>
                                {planTable.pageRows.map(s => {
                                    const state = planStatus[s.status] ?? planStatus.Open;
                                    return (
                                        <tr key={s.subjectCode}>
                                            <td>
                                                <strong>{s.subjectCode}</strong>
                                                <span className="enl-subject-title">{s.subjectTitle}</span>
                                                {/* A subject carried from an earlier year is the one a student
                                                    is most likely to think is a mistake. Label it. */}
                                                {s.isBackSubject && (
                                                    <span className="chip chip-muted enl-back-chip">
                                                        Carried from year {s.yearLevel}
                                                    </span>
                                                )}
                                            </td>
                                            <td>{s.units}</td>
                                            <td>
                                                {s.sectionCount === 0
                                                    ? '—'
                                                    : `${s.sectionCount} · ${s.seatsAvailable} seats left`}
                                            </td>
                                            <td><span className={state.chip}>{state.label}</span></td>
                                        </tr>
                                    );
                                })}
                            </tbody>
                        </table>
                        <Pagination {...planTable} />
                    </div>
                </section>
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
                                    <SortHeader label="Subject" sortKey="subjectCode" sort={mineTable.sort} onSort={mineTable.toggleSort} />
                                    <SortHeader label="Section" sortKey="sectionCode" sort={mineTable.sort} onSort={mineTable.toggleSort} />
                                    <SortHeader label="Units" sortKey="units" sort={mineTable.sort} onSort={mineTable.toggleSort} />
                                    <SortHeader label="Requested" sortKey="requestedAtUtc" sort={mineTable.sort} onSort={mineTable.toggleSort} />
                                    <SortHeader label="Status" sortKey="status" sort={mineTable.sort} onSort={mineTable.toggleSort} />
                                    <th></th>
                                </tr>
                            </thead>
                            <tbody>
                                {mineTable.pageRows.map(row => (
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
                                                    onClick={() => withdraw(row)}
                                                >Cancel</button>
                                            )}
                                            {/* withdraw() asks for confirmation itself before an
                                                approved seat is given up. */}
                                            {row.status === 'Approved' && (
                                                <button
                                                    className="btn enl-cancel" type="button"
                                                    disabled={busyId === row.requestId}
                                                    title="Give this seat back to the section"
                                                    onClick={() => withdraw(row)}
                                                >Drop</button>
                                            )}
                                        </td>
                                    </tr>
                                ))}
                            </tbody>
                        </table>
                        <Pagination {...mineTable} />
                    </div>
                </section>
            )}

            {plan?.resolved && (
                <div className="enl-scope">
                    <p className="enl-scope-text">
                        {showAll
                            ? `Showing all ${data.totalCount} published sections, including subjects outside your `
                              + 'curriculum. You can only request a seat in your own subjects.'
                            : `Showing the ${data.count} published `
                              + `${data.count === 1 ? 'section' : 'sections'} for your subjects.`}
                    </p>
                    <button className="btn btn-ghost btn-sm" type="button" onClick={toggleShowAll}>
                        {showAll ? 'Show only my subjects' : 'Show every section'}
                    </button>
                </div>
            )}

            {sections.length === 0 ? (
                <p className="enl-empty">
                    {data?.count !== 0
                        ? 'No sections match your search.'
                        : data?.totalCount > 0 && plan?.filtered
                            ? 'None of your subjects for this term have a published class yet. They appear here '
                              + 'once the Registrar publishes the schedule.'
                            : 'No published sections yet — schedules appear here once the Registrar publishes them.'}
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
                                        <span className="enl-mono">{m.day.slice(0, 3)} {hhmm(m.startMinutes)}–{hhmm(m.endMinutes)}</span>
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
