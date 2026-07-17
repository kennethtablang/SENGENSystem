import { useEffect, useState } from 'react';
import SetupModal from './SetupModal';
import { notifySuccess, notifyError } from '../shell/notify';
import { confirmAction, confirmDelete } from '../shell/confirm';
import {
    listSemesters, createSemester, updateSemester, deleteSemester, activateSemester,
    archiveSemester, unarchiveSemester, listSchoolYears
} from './api';
import { downloadSemesterExport } from '../reports/exportApi';
import './academic.css';

const terms = [
    { value: 'FirstSemester', label: 'First Semester' },
    { value: 'SecondSemester', label: 'Second Semester' }
];

function fmtDate(iso) {
    if (!iso) return '—';
    const d = new Date(`${iso}T00:00:00`);
    if (Number.isNaN(d.getTime())) return iso;
    return d.toLocaleDateString('en-PH', { year: 'numeric', month: 'short', day: '2-digit' });
}

function SemesterModal({ record, schoolYears, defaultYearId, onClose, onChanged }) {
    const isCreate = !record;
    const [form, setForm] = useState(isCreate
        ? { term: 'FirstSemester', schoolYearId: defaultYearId || '', startDate: '', endDate: '' }
        : { term: record.term, schoolYearId: record.schoolYearId || '', startDate: record.startDate, endDate: record.endDate });
    const [fieldErrors, setFieldErrors] = useState({});
    const [error, setError] = useState('');
    const [saving, setSaving] = useState(false);
    const [busy, setBusy] = useState(false);

    const set = (f) => (e) => setForm(prev => ({ ...prev, [f]: e.target.value }));
    const err = (name) => fieldErrors[name]?.[0];

    function payload() {
        return { schoolYearId: form.schoolYearId || null, term: form.term, startDate: form.startDate, endDate: form.endDate };
    }

    async function save(e) {
        e.preventDefault();
        setError(''); setFieldErrors({}); setSaving(true);
        try {
            if (isCreate) await createSemester(payload());
            else await updateSemester(record.id, payload());
            notifySuccess(isCreate ? 'Semester created.' : 'Semester updated.');
            onChanged(); onClose();
        } catch (ex) {
            setFieldErrors(ex.fieldErrors || {});
            if (!ex.fieldErrors || Object.keys(ex.fieldErrors).length === 0) setError(ex.message);
            notifyError(ex.message);
        } finally { setSaving(false); }
    }

    async function setActive() {
        setError(''); setBusy(true);
        try { await activateSemester(record.id); notifySuccess(`“${record.name}” is now the active semester.`); onChanged(); onClose(); }
        catch (ex) { setError(ex.message); notifyError(ex.message); } finally { setBusy(false); }
    }

    async function remove() {
        if (!(await confirmDelete(`semester “${record.name}”`))) return;
        setError(''); setBusy(true);
        try { await deleteSemester(record.id); notifySuccess(`Semester “${record.name}” deleted.`); onChanged(); onClose(); }
        catch (ex) { setError(ex.message); notifyError(ex.message); setBusy(false); }
    }

    // Term wrap-up: freeze the set schedule as a read-only archive once the semester is done.
    async function archive() {
        const ok = await confirmAction({
            title: `Archive “${record.name}”?`,
            message: 'Its set schedule becomes read-only history — the schedule board, generator, and publisher will refuse changes. You can reopen it later if needed.',
            confirmLabel: 'Archive'
        });
        if (!ok) return;
        setError(''); setBusy(true);
        try { await archiveSemester(record.id); notifySuccess(`“${record.name}” archived — its schedule is now frozen.`); onChanged(); onClose(); }
        catch (ex) { setError(ex.message); notifyError(ex.message); setBusy(false); }
    }

    async function reopen() {
        setError(''); setBusy(true);
        try { await unarchiveSemester(record.id); notifySuccess(`“${record.name}” reopened.`); onChanged(); onClose(); }
        catch (ex) { setError(ex.message); notifyError(ex.message); setBusy(false); }
    }

    // One-click filing: everything the term collected, in a single workbook.
    async function exportData() {
        setError(''); setBusy(true);
        try { await downloadSemesterExport(record.id); notifySuccess(`Exported the full data bundle for “${record.name}”.`); }
        catch (ex) { setError(ex.message); notifyError(ex.message); }
        finally { setBusy(false); }
    }

    const footer = (
        <>
            {!isCreate && (
                <button type="button" className="btn btn-danger setup-foot-spacer" disabled={busy} onClick={remove}>
                    Delete
                </button>
            )}
            {!isCreate && (
                <button type="button" className="btn btn-ghost" disabled={busy} onClick={exportData}
                    title="Download every report for this semester in one workbook">
                    Export data
                </button>
            )}
            {!isCreate && !record.isActive && (
                <button type="button" className="btn btn-ghost" disabled={busy} onClick={setActive}>
                    Set active
                </button>
            )}
            {!isCreate && !record.isActive && (
                record.isArchived ? (
                    <button type="button" className="btn btn-ghost" disabled={busy} onClick={reopen}>
                        Reopen
                    </button>
                ) : (
                    <button type="button" className="btn btn-ghost" disabled={busy} onClick={archive}>
                        Archive schedule
                    </button>
                )
            )}
            <button type="button" className="btn btn-ghost" onClick={onClose}>Cancel</button>
            <button type="submit" form="sem-form" className="btn btn-primary" disabled={saving}>
                {saving && <span className="spinner" aria-hidden="true" />}
                {saving ? 'Saving…' : isCreate ? 'Create' : 'Save changes'}
            </button>
        </>
    );

    return (
        <SetupModal title={isCreate ? 'New semester' : 'Edit semester'} onClose={onClose} footer={footer}>
            {error && <div className="alert">{error}</div>}
            <form id="sem-form" onSubmit={save} noValidate>
                <div className="field">
                    <label htmlFor="sem-year">School year</label>
                    <select id="sem-year" value={form.schoolYearId} onChange={set('schoolYearId')}>
                        <option value="">Select a school year…</option>
                        {schoolYears.map(y => <option key={y.id} value={y.id}>{y.name}</option>)}
                    </select>
                    {err('schoolYearId') && <p className="field-error">{err('schoolYearId')}</p>}
                </div>
                <div className="field">
                    <label htmlFor="sem-term">Semester</label>
                    <select id="sem-term" value={form.term} onChange={set('term')}>
                        {terms.map(t => <option key={t.value} value={t.value}>{t.label}</option>)}
                    </select>
                    {err('term') && <p className="field-error">{err('term')}</p>}
                </div>
                <div className="field-row">
                    <div className="field">
                        <label htmlFor="sem-start">Start date</label>
                        <input id="sem-start" type="date" value={form.startDate} onChange={set('startDate')} />
                        {err('startDate') && <p className="field-error">{err('startDate')}</p>}
                    </div>
                    <div className="field">
                        <label htmlFor="sem-end">End date</label>
                        <input id="sem-end" type="date" value={form.endDate} onChange={set('endDate')} />
                        {err('endDate') && <p className="field-error">{err('endDate')}</p>}
                    </div>
                </div>
            </form>
        </SetupModal>
    );
}

export default function SemestersPage() {
    const [rows, setRows] = useState([]);
    const [schoolYears, setSchoolYears] = useState([]);
    const [yearFilter, setYearFilter] = useState('All');
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState(null);
    const [reload, setReload] = useState(0);
    const [modal, setModal] = useState(null);

    useEffect(() => {
        let active = true;
        (async () => {
            try { const data = await listSchoolYears(); if (active) setSchoolYears(data.schoolYears); }
            catch { /* surfaced by the list load below */ }
        })();
        return () => { active = false; };
    }, [reload]);

    useEffect(() => {
        let active = true;
        (async () => {
            setLoading(true); setError(null);
            try {
                const data = await listSemesters(yearFilter === 'All' ? undefined : yearFilter);
                if (active) setRows(data.semesters);
            } catch (err) { if (active) setError(err.message); }
            finally { if (active) setLoading(false); }
        })();
        return () => { active = false; };
    }, [yearFilter, reload]);

    const refresh = () => setReload(r => r + 1);

    return (
        <div className="setup-page">
            <header className="setup-head">
                <div>
                    <h2>Semesters</h2>
                    <p className="setup-sub">
                        Manage semesters within each school year. The active semester is the term the
                        dashboard and scheduling engine work against.
                    </p>
                </div>
                <div className="setup-controls">
                    <label className="setup-filter">
                        <span>School year</span>
                        <select value={yearFilter} onChange={e => setYearFilter(e.target.value)}>
                            <option value="All">All</option>
                            {schoolYears.map(y => <option key={y.id} value={y.id}>{y.name}</option>)}
                        </select>
                    </label>
                    <button className="btn btn-primary btn-sm" type="button"
                        disabled={schoolYears.length === 0}
                        title={schoolYears.length === 0 ? 'Create a school year first' : undefined}
                        onClick={() => setModal({})}>
                        New semester
                    </button>
                </div>
            </header>

            {error && <div className="alert">{error}</div>}

            {loading ? (
                <p className="setup-empty">Loading…</p>
            ) : rows.length === 0 ? (
                <p className="setup-empty">No semesters{yearFilter !== 'All' ? ' for this school year' : ' yet'}.</p>
            ) : (
                <div className="card setup-table-wrap">
                    <table className="setup-table">
                        <thead>
                            <tr>
                                <th>Name</th>
                                <th>School year</th>
                                <th>Start</th>
                                <th>End</th>
                                <th>Status</th>
                            </tr>
                        </thead>
                        <tbody>
                            {rows.map(s => (
                                <tr key={s.id} className="setup-row" onClick={() => setModal(s)}>
                                    <td><strong>{s.name}</strong></td>
                                    <td className="setup-muted">{s.schoolYearName || '—'}</td>
                                    <td className="setup-muted">{fmtDate(s.startDate)}</td>
                                    <td className="setup-muted">{fmtDate(s.endDate)}</td>
                                    <td>
                                        {s.isActive
                                            ? <span className="chip chip-active">Active</span>
                                            : s.isArchived
                                                ? <span className="chip chip-yellow">Archived</span>
                                                : <span className="chip chip-muted">Inactive</span>}
                                    </td>
                                </tr>
                            ))}
                        </tbody>
                    </table>
                </div>
            )}

            {modal && (
                <SemesterModal
                    record={modal.id ? modal : null}
                    schoolYears={schoolYears}
                    defaultYearId={yearFilter !== 'All' ? yearFilter : ''}
                    onClose={() => setModal(null)}
                    onChanged={refresh}
                />
            )}
        </div>
    );
}
