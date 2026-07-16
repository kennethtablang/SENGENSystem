import { useEffect, useMemo, useState } from 'react';
import { listPreAuthorizations, grantPreAuthorization, revokePreAuthorization } from './api';
import { notifySuccess, notifyError } from '../shell/notify';
import { humanize, formatPHT } from '../registration/options';
import '../registration/registration.css';

/* FR-PRE-02/04: the Admission Officer clears incoming and returning students for online
   subject slot selection. The server enforces the gate — Registrar-confirmed SIS AND a
   complete document checklist — so the button simply surfaces the blockers when refused. */

const filters = ['All', 'Eligible', 'Authorized', 'Blocked'];

function eligibility(row) {
    if (row.isPreAuthorized) return 'Authorized';
    if (row.registrationStatus === 'Confirmed' && row.documentsComplete) return 'Eligible';
    return 'Blocked';
}

function PreAuthorizationPage() {
    const [rows, setRows] = useState([]);
    const [counts, setCounts] = useState({ authorizedCount: 0, eligibleCount: 0 });
    const [loading, setLoading] = useState(true);
    const [filter, setFilter] = useState('All');
    const [search, setSearch] = useState('');
    const [appliedSearch, setAppliedSearch] = useState('');
    const [reload, setReload] = useState(0);
    const [busyId, setBusyId] = useState(null);
    const [alert, setAlert] = useState(null);

    useEffect(() => {
        let active = true;
        (async () => {
            setLoading(true);
            try {
                const data = await listPreAuthorizations(appliedSearch);
                if (!active) return;
                setRows(data.students);
                setCounts({ authorizedCount: data.authorizedCount, eligibleCount: data.eligibleCount });
            } catch (err) {
                if (active) setAlert({ kind: 'error', text: err.message });
            } finally {
                if (active) setLoading(false);
            }
        })();
        return () => { active = false; };
    }, [appliedSearch, reload]);

    const visible = useMemo(
        () => filter === 'All' ? rows : rows.filter(r => eligibility(r) === filter),
        [rows, filter]);

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
                        Registrar confirms their SIS and their document checklist is complete.
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
                </div>
            </header>

            {alert && (
                <div className={alert.kind === 'success' ? 'alert alert-success' : 'alert'}>
                    <p>{alert.text}</p>
                    {alert.reasons?.length > 0 && (
                        <ul style={{ margin: '0.4rem 0 0', paddingLeft: '1.1rem' }}>
                            {alert.reasons.map((r, i) => <li key={i}>{r}</li>)}
                        </ul>
                    )}
                </div>
            )}

            {loading ? (
                <p className="reg-empty">Loading…</p>
            ) : visible.length === 0 ? (
                <p className="reg-empty">No students match this view.</p>
            ) : (
                <div className="card reg-table-wrap">
                    <table className="reg-table">
                        <thead>
                            <tr>
                                <th>Student no.</th>
                                <th>Name</th>
                                <th>Program</th>
                                <th>Type</th>
                                <th>SIS status</th>
                                <th>Requirements</th>
                                <th>Account</th>
                                <th>Authorization</th>
                                <th></th>
                            </tr>
                        </thead>
                        <tbody>
                            {visible.map(row => {
                                const state = eligibility(row);
                                return (
                                    <tr key={row.registrationId}>
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
                                                <span className={state === 'Eligible' ? 'chip chip-yellow' : 'chip chip-muted'}>
                                                    {state === 'Eligible' ? 'Ready to authorize' : 'Blocked'}
                                                </span>
                                            )}
                                        </td>
                                        <td>
                                            {row.isPreAuthorized ? (
                                                <button
                                                    className="btn" type="button"
                                                    disabled={busyId === row.registrationId}
                                                    onClick={() => revoke(row)}
                                                >Revoke</button>
                                            ) : (
                                                <button
                                                    className="btn btn-primary" type="button"
                                                    disabled={busyId === row.registrationId || state !== 'Eligible'}
                                                    title={state !== 'Eligible' ? 'Confirm the SIS and complete the checklist first' : undefined}
                                                    onClick={() => grant(row)}
                                                >Authorize</button>
                                            )}
                                        </td>
                                    </tr>
                                );
                            })}
                        </tbody>
                    </table>
                </div>
            )}

            <p style={{ marginTop: '0.9rem', fontSize: '0.8rem', color: 'var(--text-3)' }}>
                {counts.authorizedCount} authorized · {counts.eligibleCount} ready to authorize.
            </p>
        </div>
    );
}

export default PreAuthorizationPage;
