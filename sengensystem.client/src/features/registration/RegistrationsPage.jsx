import { useEffect, useState } from 'react';
import { listRegistrations, getRegistration, updateRegistration } from './api';
import { notifySuccess, notifyError } from '../shell/notify';
import { statusOptionsFor, humanize, formatPHT } from './options';
import { useServerTable } from '../shell/useServerTable';
import { SortHeader, Pagination } from '../shell/tableControls';
import './registration.css';

const statusChip = {
    Submitted: 'chip chip-muted',
    Confirmed: 'chip chip-blue',
    Rejected: 'chip chip-yellow'
};

function RegistrationsPage() {
    // The server now owns the filtering, sorting, and paging, so the page holds one page of rows
    // and the total rather than "everything up to 500" — see useServerTable.
    const [rows, setRows] = useState([]);
    const [total, setTotal] = useState(0);
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState(null);
    const [status, setStatus] = useState('All');
    const [search, setSearch] = useState('');
    const [appliedSearch, setAppliedSearch] = useState('');
    const [reload, setReload] = useState(0);
    const [selected, setSelected] = useState(null); // full detail
    const [drawerBusy, setDrawerBusy] = useState(false);
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
            setError(null);
            try {
                const data = await listRegistrations({ status, ...table.query });
                if (!active) return;
                setRows(data.registrations);
                setTotal(data.total);
            } catch (err) {
                if (active) setError(err.message);
            } finally {
                if (active) setLoading(false);
            }
        })();
        return () => { active = false; };
    // table.queryKey is a string of page/size/sort/search — depending on table.query itself would
    // refetch every render, since it is a fresh object each time.
    // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [status, table.queryKey, reload]);

    async function open(id) {
        setDrawerBusy(true);
        try {
            const detail = await getRegistration(id);
            setSelected(detail);
        } catch (err) {
            setError(err.message);
        } finally {
            setDrawerBusy(false);
        }
    }

    function setDoc(requirementCode, value) {
        setSelected(prev => ({
            ...prev,
            documents: prev.documents.map(d => d.requirementCode === requirementCode ? { ...d, status: value } : d)
        }));
    }

    async function save(nextStatus) {
        if (!selected) return;
        setDrawerBusy(true);
        setError(null);
        try {
            const payload = {
                documents: selected.documents.map(d => ({ requirementCode: d.requirementCode, status: d.status }))
            };
            if (nextStatus) payload.status = nextStatus;
            const updated = await updateRegistration(selected.id, payload);
            notifySuccess(nextStatus
                ? `Registration of ${updated.fullName} marked ${nextStatus}.`
                : `Registration of ${updated.fullName} saved.`);
            setSelected(updated);
            setReload(r => r + 1);
        } catch (err) {
            setError(err.message);
            notifyError(err.message);
        } finally {
            setDrawerBusy(false);
        }
    }

    return (
        <div className="reg-page">
            <header className="reg-head">
                <div>
                    <h2>Registration (SIS)</h2>
                    <p className="reg-sub">
                        Review digital Student Information Sheets, verify admission requirements, and confirm
                        registrations.
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
                        <span>Status</span>
                        <select value={status} onChange={e => setStatus(e.target.value)}>
                            {['All', 'Submitted', 'Confirmed', 'Rejected'].map(s => <option key={s} value={s}>{s}</option>)}
                        </select>
                    </label>
                </div>
            </header>

            {error && <div className="alert">{error}</div>}

            {loading ? (
                <p className="reg-empty">Loading…</p>
            ) : rows.length === 0 ? (
                <p className="reg-empty">No registrations found.</p>
            ) : (
                <div className="card reg-table-wrap">
                    <table className="reg-table">
                        <thead>
                            <tr>
                                <SortHeader label="Student no." sortKey="studentNumber" sort={table.sort} onSort={table.toggleSort} />
                                <SortHeader label="Name" sortKey="fullName" sort={table.sort} onSort={table.toggleSort} />
                                <SortHeader label="Program" sortKey="program" sort={table.sort} onSort={table.toggleSort} />
                                <SortHeader label="Type" sortKey="studentType" sort={table.sort} onSort={table.toggleSort} />
                                <SortHeader label="Requirements" sortKey="documentsSubmitted" sort={table.sort} onSort={table.toggleSort} />
                                <SortHeader label="Submitted" sortKey="createdAtUtc" sort={table.sort} onSort={table.toggleSort} />
                                <SortHeader label="Status" sortKey="status" sort={table.sort} onSort={table.toggleSort} />
                            </tr>
                        </thead>
                        <tbody>
                            {table.pageRows.map(r => (
                                <tr key={r.id} className="reg-row" onClick={() => open(r.id)}>
                                    <td className="reg-mono">{r.studentNumber}</td>
                                    <td><strong>{r.fullName}</strong></td>
                                    <td>{r.program}</td>
                                    <td>{humanize(r.studentType)}</td>
                                    <td>{r.documentsSubmitted}/{r.documentsTotal}</td>
                                    <td className="reg-when">{formatPHT(r.createdAtUtc)}</td>
                                    <td><span className={statusChip[r.status] || 'chip chip-muted'}>{r.status}</span></td>
                                </tr>
                            ))}
                        </tbody>
                    </table>
                    <Pagination {...table} />
                </div>
            )}

            {selected && (
                <div className="reg-drawer-backdrop" onClick={() => setSelected(null)}>
                    <aside className="reg-drawer" onClick={e => e.stopPropagation()}>
                        <header className="reg-drawer-head">
                            <div>
                                <h3>{selected.fullName}</h3>
                                <span className="reg-mono">{selected.studentNumber}</span>
                                <span className={statusChip[selected.status] || 'chip chip-muted'}>{selected.status}</span>
                            </div>
                            <button className="reg-drawer-close" onClick={() => setSelected(null)} aria-label="Close">×</button>
                        </header>

                        <div className="reg-drawer-body">
                            <section className="reg-detail-grid">
                                <div><span>Type</span><strong>{humanize(selected.studentType)}</strong></div>
                                <div><span>Program</span><strong>{selected.program}</strong></div>
                                <div><span>Email</span><strong>{selected.email}</strong></div>
                                <div><span>Mobile</span><strong>{selected.mobileNumber}</strong></div>
                                <div><span>Date of birth</span><strong>{selected.dateOfBirth}</strong></div>
                                <div><span>Gender</span><strong>{selected.gender}</strong></div>
                                <div className="reg-detail-wide"><span>Address</span><strong>
                                    {[selected.addressLine, selected.barangay, selected.cityMunicipality, selected.province, selected.zipCode].filter(Boolean).join(', ')}
                                </strong></div>
                                <div className="reg-detail-wide"><span>Last school</span><strong>
                                    {selected.schoolName} · {humanize(selected.lastSchoolLevel)}{selected.schoolYear ? ` · ${selected.schoolYear}` : ''}
                                </strong></div>
                                <div className="reg-detail-wide"><span>Guardian</span><strong>
                                    {selected.guardianName} ({humanize(selected.guardianRelationship)}) · {selected.guardianMobile}
                                </strong></div>
                                <div><span>Submitted</span><strong>{formatPHT(selected.createdAtUtc)}</strong></div>
                                <div><span>Terms accepted</span><strong>{formatPHT(selected.termsAcceptedAtUtc)}</strong></div>
                            </section>

                            <section>
                                <h4 className="reg-detail-title">Admission requirements</h4>
                                {selected.documents.length === 0 ? (
                                    <p className="reg-empty">No requirements apply to this student's program.</p>
                                ) : (
                                    <ul className="reg-doc-list">
                                        {selected.documents.map(doc => (
                                            <li key={doc.requirementCode}>
                                                <span className="reg-doc-label">{doc.label}</span>
                                                <select
                                                    value={doc.status}
                                                    onChange={e => setDoc(doc.requirementCode, e.target.value)}
                                                >
                                                    {statusOptionsFor(doc.statuses).map(o => (
                                                        <option key={o.value} value={o.value}>{o.label}</option>
                                                    ))}
                                                </select>
                                            </li>
                                        ))}
                                    </ul>
                                )}
                            </section>
                        </div>

                        <footer className="reg-drawer-foot">
                            <button className="btn btn-ghost" disabled={drawerBusy} onClick={() => save(null)}>
                                Save checklist
                            </button>
                            <div className="reg-drawer-foot-right">
                                {selected.status !== 'Rejected' && (
                                    <button className="btn btn-ghost" disabled={drawerBusy} onClick={() => save('Rejected')}>
                                        Reject
                                    </button>
                                )}
                                {selected.status !== 'Confirmed' && (
                                    <button className="btn btn-primary" disabled={drawerBusy} onClick={() => save('Confirmed')}>
                                        {drawerBusy ? 'Saving…' : 'Confirm registration'}
                                    </button>
                                )}
                            </div>
                        </footer>
                    </aside>
                </div>
            )}
        </div>
    );
}

export default RegistrationsPage;
