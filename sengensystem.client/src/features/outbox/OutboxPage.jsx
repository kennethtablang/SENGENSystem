import { useEffect, useState } from 'react';
import { listOutbox, retryOutboxEmail, retryAllFailed } from './api';
import { notifySuccess, notifyError } from '../shell/notify';
import { confirmAction } from '../shell/confirm';
import { formatPHT } from '../registration/options';
import { useServerTable } from '../shell/useServerTable';
import { SortHeader, Pagination } from '../shell/tableControls';
import '../registration/registration.css';
import './outbox.css';

/* Operational visibility over transactional email.

   Bulk mail is queued rather than sent inline, which fixed the timing problem — a term-sized sweep
   no longer runs inside the Registrar's request. It also started recording something that was
   previously swallowed by design: a delivery that failed. This page is what makes that record
   worth having. "Did that student actually get the email?" is the question, and before this the
   only way to ask it was to query the table by hand. */

const VIEWS = [
    { key: 'All', label: 'All' },
    { key: 'Pending', label: 'Queued' },
    { key: 'Sent', label: 'Sent' },
    { key: 'Failed', label: 'Failed' }
];

const statusChip = {
    Pending: 'chip chip-yellow',
    Sent: 'chip chip-blue',
    Failed: 'chip chip-muted'
};

const statusLabel = { Pending: 'Queued', Sent: 'Sent', Failed: 'Failed' };

export default function OutboxPage() {
    const [rows, setRows] = useState([]);
    const [total, setTotal] = useState(0);
    const [counts, setCounts] = useState({ pendingCount: 0, sentCount: 0, failedCount: 0 });
    const [loading, setLoading] = useState(true);
    const [status, setStatus] = useState('All');
    const [search, setSearch] = useState('');
    const [appliedSearch, setAppliedSearch] = useState('');
    const [reload, setReload] = useState(0);
    const [busy, setBusy] = useState(false);
    const [alert, setAlert] = useState(null);

    const table = useServerTable({
        rows,
        total,
        initialSort: { key: 'createdAtUtc', dir: 'desc' },
        search: appliedSearch
    });

    useEffect(() => {
        let active = true;
        (async () => {
            setLoading(true);
            try {
                const data = await listOutbox({ status, ...table.query });
                if (!active) return;
                setRows(data.emails);
                setTotal(data.total);
                setCounts({
                    pendingCount: data.pendingCount,
                    sentCount: data.sentCount,
                    failedCount: data.failedCount
                });
            } catch (err) {
                if (active) setAlert({ kind: 'error', text: err.message });
            } finally {
                if (active) setLoading(false);
            }
        })();
        return () => { active = false; };
    // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [status, table.queryKey, reload]);

    async function retryOne(row) {
        setBusy(true);
        try {
            const result = await retryOutboxEmail(row.id);
            notifySuccess(result.message);
            setReload(v => v + 1);
        } catch (err) {
            setAlert({ kind: 'error', text: err.message });
            notifyError(err.message);
        } finally {
            setBusy(false);
        }
    }

    async function retryEverything() {
        // Confirmed, because it re-sends to every failed address at once — and an address that
        // failed five times is quite often one that will never work, so a thoughtless retry is a
        // burst of mail at a dead mailbox.
        const ok = await confirmAction({
            title: `Retry ${counts.failedCount} failed email${counts.failedCount === 1 ? '' : 's'}?`,
            message: 'Each will be attempted up to five more times. Do this after fixing a mail '
                + 'outage — retrying an address that is simply wrong will only fail again.',
            confirmLabel: 'Retry all'
        });
        if (!ok) return;

        setBusy(true);
        try {
            const result = await retryAllFailed();
            notifySuccess(result.message);
            setReload(v => v + 1);
        } catch (err) {
            setAlert({ kind: 'error', text: err.message });
            notifyError(err.message);
        } finally {
            setBusy(false);
        }
    }

    return (
        <div className="reg-page">
            <header className="reg-head">
                <div>
                    <h2>Email outbox</h2>
                    <p className="reg-sub">
                        Every notification the system has queued, delivered, or given up on. Bulk mail —
                        document reminders, enlistment approvals, schedule publications — is queued here and
                        sent in the background, retrying with a widening delay before it is recorded as
                        failed. Password resets and sign-in codes go out directly and do not appear.
                    </p>
                </div>
                <div className="reg-controls">
                    <form onSubmit={e => { e.preventDefault(); setAppliedSearch(search); }} className="reg-search">
                        <input
                            type="search" placeholder="Search address or subject"
                            value={search} onChange={e => setSearch(e.target.value)}
                        />
                    </form>
                    <label className="reg-filter">
                        <span>Show</span>
                        <select value={status} onChange={e => setStatus(e.target.value)}>
                            {VIEWS.map(v => <option key={v.key} value={v.key}>{v.label}</option>)}
                        </select>
                    </label>
                </div>
            </header>

            {alert && <div className="alert" role="alert">{alert.text}</div>}

            {/* The number worth acting on, stated where it can be acted on. A failure that is only
                visible by filtering to it is a failure nobody looks for. */}
            {counts.failedCount > 0 && (
                <div className="outbox-failed-banner" role="status">
                    <span>
                        <strong>{counts.failedCount}</strong> email
                        {counts.failedCount === 1 ? ' has' : 's have'} failed permanently — the recipients
                        were never told. Fix the cause, then retry.
                    </span>
                    <button type="button" className="btn btn-sm" disabled={busy} onClick={retryEverything}>
                        Retry all failed
                    </button>
                </div>
            )}

            {loading ? (
                <p className="reg-empty">Loading…</p>
            ) : rows.length === 0 ? (
                <p className="reg-empty">
                    {appliedSearch ? 'No emails match your search.' : 'Nothing in the outbox for this view.'}
                </p>
            ) : (
                <div className="card reg-table-wrap">
                    <table className="reg-table">
                        <thead>
                            <tr>
                                <SortHeader label="Recipient" sortKey="toEmail" sort={table.sort} onSort={table.toggleSort} />
                                <SortHeader label="Subject" sortKey="subject" sort={table.sort} onSort={table.toggleSort} />
                                <SortHeader label="Kind" sortKey="kind" sort={table.sort} onSort={table.toggleSort} />
                                <SortHeader label="Status" sortKey="status" sort={table.sort} onSort={table.toggleSort} />
                                <SortHeader label="Attempts" sortKey="attempts" sort={table.sort} onSort={table.toggleSort} />
                                <th>Queued</th>
                                <th></th>
                            </tr>
                        </thead>
                        <tbody>
                            {table.pageRows.map(row => (
                                <tr key={row.id}>
                                    <td>
                                        <strong>{row.toEmail}</strong>
                                        {row.toName && <span className="reg-when">{row.toName}</span>}
                                    </td>
                                    <td style={{ whiteSpace: 'normal' }}>
                                        {row.subject}
                                        {/* The error text is the whole point of recording a failure —
                                            "did that student get the email?" is answered by *why not*. */}
                                        {row.lastError && (
                                            <span className="outbox-error">{row.lastError}</span>
                                        )}
                                    </td>
                                    <td>{row.kind || '—'}</td>
                                    <td>
                                        <span className={statusChip[row.status] || 'chip chip-muted'}>
                                            {statusLabel[row.status] || row.status}
                                        </span>
                                        {row.sentAtUtc && <span className="reg-when">{formatPHT(row.sentAtUtc)}</span>}
                                    </td>
                                    <td>{row.attempts}</td>
                                    <td>
                                        <span className="reg-when">{formatPHT(row.createdAtUtc)}</span>
                                    </td>
                                    <td className="reg-actions">
                                        {row.status === 'Failed' && (
                                            <button
                                                type="button" className="btn btn-sm" disabled={busy}
                                                onClick={() => retryOne(row)}
                                            >
                                                Retry
                                            </button>
                                        )}
                                    </td>
                                </tr>
                            ))}
                        </tbody>
                    </table>
                    <Pagination {...table} />
                </div>
            )}

            <p className="doc-footnote">
                {counts.pendingCount} queued · {counts.sentCount} sent · {counts.failedCount} failed.
            </p>
        </div>
    );
}
