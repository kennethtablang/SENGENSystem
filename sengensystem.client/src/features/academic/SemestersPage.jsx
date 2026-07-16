import { useEffect, useState } from 'react';
import SetupModal from './SetupModal';
import {
    listSemesters, createSemester, updateSemester, deleteSemester, activateSemester, listSchoolYears
} from './api';
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
            onChanged(); onClose();
        } catch (ex) {
            setFieldErrors(ex.fieldErrors || {});
            if (!ex.fieldErrors || Object.keys(ex.fieldErrors).length === 0) setError(ex.message);
        } finally { setSaving(false); }
    }

    async function setActive() {
        setError(''); setBusy(true);
        try { await activateSemester(record.id); onChanged(); onClose(); }
        catch (ex) { setError(ex.message); } finally { setBusy(false); }
    }

    async function remove() {
        if (!window.confirm(`Delete semester “${record.name}”? This can't be undone.`)) return;
        setError(''); setBusy(true);
        try { await deleteSemester(record.id); onChanged(); onClose(); }
        catch (ex) { setError(ex.message); setBusy(false); }
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
