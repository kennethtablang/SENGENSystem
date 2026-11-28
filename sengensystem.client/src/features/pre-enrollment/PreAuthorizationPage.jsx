import { useEffect, useState } from 'react';
import {
    listPreAuthorizations, grantPreAuthorization, bulkGrantPreAuthorization, revokePreAuthorization
} from './api';
import { notifySuccess, notifyError } from '../shell/notify';
import { confirmAction } from '../shell/confirm';
import { humanize, formatPHT } from '../registration/options';
import { useServerTable } from '../shell/useServerTable';
import { SortHeader, Pagination } from '../shell/tableControls';
import '../registration/registration.css';

/* FR-PRE-02/04: the Admission Officer clears incoming and returning students for online
   subject slot selection. The server enforces the gate — a Registrar-confirmed SIS AND the
   papers marked required for authorization (report card + good moral for a new enrollee,
   transcript + certificate of transfer for a transferee). The rest of the checklist may still
   be arriving, so it is shown for follow-up rather than treated as a blocker.

   Bulk clearance: "Start selecting students" adds a checkbox column. Only students who are ready to
   authorize can be ticked, the selection survives paging and filter changes (so a list can be
   built across the whole queue), and the server re-checks every one — anyone who turns out to be
   blocked is skipped and reported with the reason, the rest are cleared. */

const filters = ['All', 'Eligible', 'Authorized', 'Blocked'];

const missingRequired = (row) => row.missingAuthorizationRequirements ?? [];

function eligibility(row) {
    if (row.isPreAuthorized) return 'Authorized';
    if (row.registrationStatus === 'Confirmed' && missingRequired(row).length === 0) return 'Eligible';
    return 'Blocked';
}

/** Why this student can't be cleared yet — the same reasons the server would refuse with. */
function blockers(row) {
    const reasons = [];
    if (row.registrationStatus !== 'Confirmed') {
        reasons.push(`SIS is ${row.registrationStatus} — the Registrar must confirm it first`);
    }
    reasons.push(...missingRequired(row).map(name => `${name} not submitted`));
    return reasons;
}

function PreAuthorizationPage() {
    const [rows, setRows] = useState([]);
    const [total, setTotal] = useState(0);
    const [counts, setCounts] = useState({ authorizedCount: 0, eligibleCount: 0 });
    const [loading, setLoading] = useState(true);
    const [filter, setFilter] = useState('All');
    const [search, setSearch] = useState('');
    const [appliedSearch, setAppliedSearch] = useState('');
    const [reload, setReload] = useState(0);
    const [busyId, setBusyId] = useState(null);
    const [alert, setAlert] = useState(null);
    // Selection mode for bulk clearance. `selected` maps registrationId -> full name, so the bar
    // and the confirm dialog can name students who are on another page.
    const [selecting, setSelecting] = useState(false);
    const [selected, setSelected] = useState(() => new Map());
    const [bulkBusy, setBulkBusy] = useState(false);

    // The eligibility chip is a server-side filter now. Applied in the browser it would narrow
    // whichever page had been fetched, so "Eligible" would show only the eligible students who
    // happened to land on page 1 — and the pager would still count the others.
    const table = useServerTable({
        rows,
        total,
        initialSort: { key: 'fullName', dir: 'asc' },
        search: appliedSearch
    });

    useEffect(() => {
        let active = true;
        (async () => {
            setLoading(true);
            try {
                const data = await listPreAuthorizations({ filter, ...table.query });
                if (!active) return;
                setRows(data.students);
                setTotal(data.total);
                // Counted server-side across the whole queue, independent of the chip, so the
                // summary stays a statement about the term rather than about the current view.
                setCounts({ authorizedCount: data.authorizedCount, eligibleCount: data.eligibleCount });
            } catch (err) {
                if (active) setAlert({ kind: 'error', text: err.message });
            } finally {
                if (active) setLoading(false);
            }
        })();
        return () => { active = false; };
    // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [filter, table.queryKey, reload]);

    async function grant(row) {
        setBusyId(row.registrationId);
        setAlert(null);
        try {
            await grantPreAuthorization(row.registrationId);
            setAlert({ kind: 'success', text: `${row.fullName} is now cleared for subject enlistment.` });
            notifySuccess(`${row.fullName} is now cleared for subject enlistment.`);
            setReload(v => v + 1);
        } catch (err) {
            setAlert({ kind: 'error', text: err.message, reasons: err.reasons });
            notifyError(err.message);
        } finally {
            setBusyId(null);
        }
    }

    // ---- Bulk clearance ----

    const pageEligible = table.pageRows.filter(r => eligibility(r) === 'Eligible');
    const allPageSelected = pageEligible.length > 0 && pageEligible.every(r => selected.has(r.registrationId));

    function startSelecting() {
        setSelected(new Map());
        setSelecting(true);
        setAlert(null);
    }

    function stopSelecting() {
        setSelecting(false);
        setSelected(new Map());
    }

    function toggleRow(row) {
        setSelected(prev => {
            const next = new Map(prev);
            if (next.has(row.registrationId)) next.delete(row.registrationId);
            else next.set(row.registrationId, row.fullName);
            return next;
        });
    }

    function togglePage() {
        setSelected(prev => {
            const next = new Map(prev);
            for (const r of pageEligible) {
                if (allPageSelected) next.delete(r.registrationId);
                else next.set(r.registrationId, r.fullName);
            }
            return next;
        });
    }

    async function authorizeSelected() {
        const count = selected.size;
        if (count === 0) return;
        const names = [...selected.values()];
        const ok = await confirmAction({
            title: `Authorize ${count} student${count === 1 ? '' : 's'}?`,
            message: `${names.slice(0, 5).join(', ')}${count > 5 ? ` and ${count - 5} more` : ''} will be `
                + 'cleared for online subject slot selection. Each is re-checked first — anyone whose SIS '
                + 'is no longer confirmed or who is missing a required paper is skipped and listed.',
            confirmLabel: `Authorize ${count}`
        });
        if (!ok) return;

        setBulkBusy(true);
        setAlert(null);
        try {
            const result = await bulkGrantPreAuthorization([...selected.keys()]);
            const skipped = result.outcomes.filter(o => !o.authorized);
            const text = `Authorized ${result.authorizedCount} student${result.authorizedCount === 1 ? '' : 's'}`
                + (skipped.length > 0 ? ` · ${skipped.length} skipped.` : '.');
            setAlert({
                kind: result.authorizedCount === 0 ? 'error' : 'success',
                text,
                reasons: skipped.map(o =>
                    `${o.studentNumber ? `${o.studentNumber} · ` : ''}${o.fullName}: ${o.reasons.join(' ')}`)
            });
            if (result.authorizedCount > 0) notifySuccess(text);
            else notifyError(text);
            stopSelecting();
            setReload(v => v + 1);
        } catch (err) {
            setAlert({ kind: 'error', text: err.message });
            notifyError(err.message);
        } finally {
            setBulkBusy(false);
        }
    }

    async function revoke(row) {
        setBusyId(row.registrationId);
        setAlert(null);
        try {
            await revokePreAuthorization(row.registrationId);
            setAlert({ kind: 'success', text: `Revoked the clearance of ${row.fullName}.` });
            notifySuccess(`Revoked the clearance of ${row.fullName}.`);
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
                    <h2>Pre-authorization</h2>
                    <p className="reg-sub">
                        Clear students for online subject slot selection. A student becomes eligible once the
                        Registrar confirms their SIS and the papers required for authorization are on file —
                        the report card and good moral for a new enrollee, the transcript and certificate of
                        transfer for a transferee. The remaining requirements can follow.
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
                        <span>Show</span>
                        <select value={filter} onChange={e => setFilter(e.target.value)}>
                            {filters.map(f => <option key={f} value={f}>{f}</option>)}
                        </select>
                    </label>
                    {!selecting && (
                        <button
                            className="btn btn-primary" type="button"
                            disabled={counts.eligibleCount === 0}
                            title={counts.eligibleCount === 0
                                ? 'No students are ready to authorize yet'
                                : 'Tick the students to clear, then authorize them all at once'}
                            onClick={startSelecting}
                        >
                            Start selecting students
                        </button>
                    )}
                </div>
            </header>

            {selecting && (
                <div className="preauth-bulkbar" role="region" aria-label="Bulk authorization">
                    <span>
                        <strong>{selected.size}</strong> student{selected.size === 1 ? '' : 's'} selected
                        <span className="preauth-bulkbar-hint">
                            {' '}— only students ready to authorize can be ticked; the selection is kept across pages.
                        </span>
                    </span>
                    <button
                        className="btn btn-sm btn-ghost" type="button"
                        disabled={bulkBusy || pageEligible.length === 0}
                        onClick={togglePage}
                    >
                        {allPageSelected ? 'Unselect this page' : 'Select all on this page'}
                    </button>
                    <button className="btn btn-sm btn-ghost" type="button" disabled={bulkBusy} onClick={stopSelecting}>
                        Cancel
                    </button>
                    <button
                        className="btn btn-sm btn-primary" type="button"
                        disabled={bulkBusy || selected.size === 0}
                        onClick={authorizeSelected}
                    >
                        {bulkBusy ? 'Authorizing…' : `Authorize ${selected.size || ''}`.trim()}
                    </button>
                </div>
            )}

            {alert && (
                <div className={alert.kind === 'success' ? 'alert alert-success' : 'alert'}
                    role={alert.kind === 'success' ? 'status' : 'alert'}>
                    <p>{alert.text}</p>
                    {alert.reasons?.length > 0 && (
                        <ul className="preauth-skipped">
                            {alert.reasons.map((r, i) => <li key={i}>{r}</li>)}
                        </ul>
                    )}
                </div>
            )}

            {loading ? (
                <p className="reg-empty">Loading…</p>
            ) : rows.length === 0 ? (
                <p className="reg-empty">No students match this view.</p>
            ) : (
                <div className="card reg-table-wrap">
                    <table className="reg-table">
                        <thead>
                            <tr>
                                {selecting && (
                                    <th className="preauth-check-col">
                                        <input
                                            type="checkbox"
                                            aria-label="Select every student ready to authorize on this page"
                                            checked={allPageSelected}
                                            disabled={bulkBusy || pageEligible.length === 0}
                                            onChange={togglePage}
                                        />
                                    </th>
                                )}
                                <SortHeader label="Student no." sortKey="studentNumber" sort={table.sort} onSort={table.toggleSort} />
                                <SortHeader label="Name" sortKey="fullName" sort={table.sort} onSort={table.toggleSort} />
                                <SortHeader label="Program" sortKey="program" sort={table.sort} onSort={table.toggleSort} />
                                <SortHeader label="Type" sortKey="studentType" sort={table.sort} onSort={table.toggleSort} />
                                <SortHeader label="SIS status" sortKey="registrationStatus" sort={table.sort} onSort={table.toggleSort} />
                                <SortHeader label="Requirements" sortKey="submittedCount" sort={table.sort} onSort={table.toggleSort} />
                                <SortHeader label="Account" sortKey="hasLinkedAccount" sort={table.sort} onSort={table.toggleSort} />
                                <SortHeader label="Authorization" sortKey="authorization" sort={table.sort} onSort={table.toggleSort} />
                                <th></th>
                            </tr>
                        </thead>
                        <tbody>
                            {table.pageRows.map(row => {
                                const state = eligibility(row);
                                return (
                                    <tr
                                        key={row.registrationId}
                                        className={selected.has(row.registrationId) ? 'is-selected' : undefined}
                                    >
                                        {selecting && (
                                            <td className="preauth-check-col">
                                                <input
                                                    type="checkbox"
                                                    aria-label={`Select ${row.fullName}`}
                                                    checked={selected.has(row.registrationId)}
                                                    disabled={bulkBusy || state !== 'Eligible'}
                                                    title={state === 'Authorized' ? 'Already authorized'
                                                        : state === 'Blocked' ? blockers(row).join(' · ') : undefined}
                                                    onChange={() => toggleRow(row)}
                                                />
                                            </td>
                                        )}
                                        <td className="reg-mono">{row.studentNumber}</td>
                                        <td><strong>{row.fullName}</strong></td>
                                        <td>{row.program}</td>
                                        <td>{humanize(row.studentType)}</td>
                                        <td>
                                            <span className={row.registrationStatus === 'Confirmed' ? 'chip chip-blue' : 'chip chip-muted'}>
                                                {row.registrationStatus}
                                            </span>
                                        </td>
                                        <td>
                                            <span className={row.documentsComplete ? 'chip chip-blue' : 'chip chip-yellow'}>
                                                {row.submittedCount}/{row.totalCount}
                                            </span>
                                        </td>
                                        <td>{row.hasLinkedAccount ? 'Linked' : '—'}</td>
                                        <td>
                                            {row.isPreAuthorized ? (
                                                <span className="chip chip-blue" title={formatPHT(row.preAuthorizedAtUtc)}>
                                                    Authorized
                                                </span>
                                            ) : (
                                                <span
                                                    className={state === 'Eligible' ? 'chip chip-yellow' : 'chip chip-muted'}
                                                    title={state === 'Eligible' ? undefined : blockers(row).join(' · ')}
                                                >
                                                    {state === 'Eligible' ? 'Ready to authorize' : 'Blocked'}
                                                </span>
                                            )}
                                            {!row.isPreAuthorized && missingRequired(row).length > 0 && (
                                                <div className="reg-when">
                                                    Waiting on {missingRequired(row).join(', ')}
                                                </div>
                                            )}
                                        </td>
                                        <td>
                                            {row.isPreAuthorized ? (
                                                <button
                                                    className="btn" type="button"
                                                    disabled={bulkBusy || busyId === row.registrationId}
                                                    onClick={() => revoke(row)}
                                                >Revoke</button>
                                            ) : (
                                                <button
                                                    className="btn btn-primary" type="button"
                                                    disabled={bulkBusy || busyId === row.registrationId || state !== 'Eligible'}
                                                    title={state !== 'Eligible' ? blockers(row).join(' · ') : undefined}
                                                    onClick={() => grant(row)}
                                                >Authorize</button>
                                            )}
                                        </td>
                                    </tr>
                                );
                            })}
                        </tbody>
                    </table>
                    <Pagination {...table} />
                </div>
            )}

            <p style={{ marginTop: '0.9rem', fontSize: '0.8rem', color: 'var(--text-3)' }}>
                {counts.authorizedCount} authorized · {counts.eligibleCount} ready to authorize.
            </p>
        </div>
    );
}

export default PreAuthorizationPage;
