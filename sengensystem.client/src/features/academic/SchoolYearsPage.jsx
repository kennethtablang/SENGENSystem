import { useEffect, useState } from 'react';
import SetupModal from './SetupModal';
import { notifySuccess, notifyError } from '../shell/notify';
import { confirmDelete } from '../shell/confirm';
import { useTableControls } from '../shell/useTableControls';
import { SortHeader, Pagination, TableSearch } from '../shell/tableControls';
import {
    listSchoolYears, createSchoolYear, updateSchoolYear, deleteSchoolYear, activateSchoolYear
} from './api';
import './academic.css';

const blank = { name: '', startDate: '', endDate: '' };

function fmtDate(iso) {
    if (!iso) return '—';
    const d = new Date(`${iso}T00:00:00`);
    if (Number.isNaN(d.getTime())) return iso;
    return d.toLocaleDateString('en-PH', { year: 'numeric', month: 'short', day: '2-digit' });
}

function SchoolYearModal({ record, onClose, onChanged }) {
    const isCreate = !record;
    const [form, setForm] = useState(isCreate ? blank : {
        name: record.name, startDate: record.startDate, endDate: record.endDate
    });
    const [fieldErrors, setFieldErrors] = useState({});
    const [error, setError] = useState('');
    const [saving, setSaving] = useState(false);
    const [busy, setBusy] = useState(false);

    const set = (f) => (e) => setForm(prev => ({ ...prev, [f]: e.target.value }));
    const err = (name) => fieldErrors[name]?.[0];

    async function save(e) {
        e.preventDefault();
        setError(''); setFieldErrors({}); setSaving(true);
        try {
            if (isCreate) await createSchoolYear(form);
            else await updateSchoolYear(record.id, form);
            notifySuccess(isCreate ? 'School year created.' : 'School year updated.');
            onChanged(); onClose();
        } catch (ex) {
            setFieldErrors(ex.fieldErrors || {});
            if (!ex.fieldErrors || Object.keys(ex.fieldErrors).length === 0) setError(ex.message);
            notifyError(ex.message);
        } finally { setSaving(false); }
    }

    async function setActive() {
        setError(''); setBusy(true);
        try { await activateSchoolYear(record.id); notifySuccess(`“${record.name}” is now the active school year.`); onChanged(); onClose(); }
        catch (ex) { setError(ex.message); notifyError(ex.message); } finally { setBusy(false); }
    }

    async function remove() {
        if (!(await confirmDelete(`school year “${record.name}”`))) return;
        setError(''); setBusy(true);
        try { await deleteSchoolYear(record.id); notifySuccess(`School year “${record.name}” deleted.`); onChanged(); onClose(); }
        catch (ex) { setError(ex.message); notifyError(ex.message); setBusy(false); }
    }

    const footer = (
        <>
            {!isCreate && (
                <button type="button" className="btn btn-danger setup-foot-spacer" disabled={busy} onClick={remove}>
                    Delete
                </button>
            )}
            {!isCreate && !record.isActive && (
                <button type="button" className="btn btn-ghost" disabled={busy} onClick={setActive}>
                    Set active
                </button>
            )}
            <button type="button" className="btn btn-ghost" onClick={onClose}>Cancel</button>
            <button type="submit" form="sy-form" className="btn btn-primary" disabled={saving}>
                {saving && <span className="spinner" aria-hidden="true" />}
                {saving ? 'Saving…' : isCreate ? 'Create' : 'Save changes'}
            </button>
        </>
    );

    return (
        <SetupModal title={isCreate ? 'New school year' : 'Edit school year'} onClose={onClose} footer={footer}>
            {error && <div className="alert">{error}</div>}
            <form id="sy-form" onSubmit={save} noValidate>
                <div className="field">
                    <label htmlFor="sy-name">Name</label>
                    <input id="sy-name" value={form.name} onChange={set('name')} autoComplete="off" placeholder="AY 2026-2027" />
                    {err('name') && <p className="field-error">{err('name')}</p>}
                </div>
                <div className="field-row">
                    <div className="field">
                        <label htmlFor="sy-start">Start date</label>
                        <input id="sy-start" type="date" value={form.startDate} onChange={set('startDate')} />
                        {err('startDate') && <p className="field-error">{err('startDate')}</p>}
                    </div>
                    <div className="field">
                        <label htmlFor="sy-end">End date</label>
                        <input id="sy-end" type="date" value={form.endDate} onChange={set('endDate')} />
                        {err('endDate') && <p className="field-error">{err('endDate')}</p>}
                    </div>
                </div>
            </form>
        </SetupModal>
    );
}

export default function SchoolYearsPage() {
    const [rows, setRows] = useState([]);
    const [search, setSearch] = useState('');
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState(null);
    const [reload, setReload] = useState(0);
    const [modal, setModal] = useState(null); // null | {} (create) | record (edit)

    useEffect(() => {
        let active = true;
        (async () => {
            setLoading(true); setError(null);
            try { const data = await listSchoolYears(); if (active) setRows(data.schoolYears); }
            catch (err) { if (active) setError(err.message); }
            finally { if (active) setLoading(false); }
        })();
        return () => { active = false; };
    }, [reload]);

    const refresh = () => setReload(r => r + 1);

    const table = useTableControls(rows, {
        columns: {
            name: y => y.name,
            startDate: y => y.startDate,
            endDate: y => y.endDate,
            semesterCount: y => y.semesterCount,
            status: y => (y.isActive ? 0 : 1)
        },
        initialSort: { key: 'startDate', dir: 'desc' },
        search,
        searchFields: [y => y.name]
    });

    return (
        <div className="setup-page">
            <header className="setup-head">
                <div>
                    <h2>School years</h2>
                    <p className="setup-sub">
                        Define academic school years and mark the active one. Each school year groups its
                        semesters.
                    </p>
                </div>
                <div className="setup-controls">
                    <TableSearch value={search} onChange={setSearch} placeholder="Filter school year…" />
                    <button className="btn btn-primary btn-sm" type="button" onClick={() => setModal({})}>
                        New school year
                    </button>
                </div>
            </header>

            {error && <div className="alert">{error}</div>}

            {loading ? (
                <p className="setup-empty">Loading…</p>
            ) : table.total === 0 ? (
                <p className="setup-empty">
                    {search ? 'No school years match your filter.' : 'No school years yet.'}
                </p>
            ) : (
                <div className="card setup-table-wrap">
                    <table className="setup-table">
                        <thead>
                            <tr>
                                <SortHeader label="Name" sortKey="name" sort={table.sort} onSort={table.toggleSort} />
                                <SortHeader label="Start" sortKey="startDate" sort={table.sort} onSort={table.toggleSort} />
                                <SortHeader label="End" sortKey="endDate" sort={table.sort} onSort={table.toggleSort} />
                                <SortHeader label="Semesters" sortKey="semesterCount" sort={table.sort} onSort={table.toggleSort} />
                                <SortHeader label="Status" sortKey="status" sort={table.sort} onSort={table.toggleSort} />
                            </tr>
                        </thead>
                        <tbody>
                            {table.pageRows.map(y => (
                                <tr key={y.id} className="setup-row" onClick={() => setModal(y)}>
                                    <td><strong>{y.name}</strong></td>
                                    <td className="setup-muted">{fmtDate(y.startDate)}</td>
                                    <td className="setup-muted">{fmtDate(y.endDate)}</td>
                                    <td className="setup-num">{y.semesterCount}</td>
                                    <td>
                                        {y.isActive
                                            ? <span className="chip chip-active">Active</span>
                                            : <span className="chip chip-muted">Inactive</span>}
                                    </td>
                                </tr>
                            ))}
                        </tbody>
                    </table>
                    <Pagination {...table} />
                </div>
            )}

            {modal && (
                <SchoolYearModal
                    record={modal.id ? modal : null}
                    onClose={() => setModal(null)}
                    onChanged={refresh}
                />
            )}
        </div>
    );
}
