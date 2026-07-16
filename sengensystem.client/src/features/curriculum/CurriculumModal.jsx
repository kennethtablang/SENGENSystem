import { useState } from 'react';
import SetupModal from '../academic/SetupModal';
import { createCurriculum, updateCurriculum, deleteCurriculum, activateCurriculum } from './api';

export default function CurriculumModal({ record, schoolYears, onClose, onChanged, onDeleted }) {
    const isCreate = !record;
    const [form, setForm] = useState(isCreate
        ? { programCode: '', programName: '' }
        : { programCode: record.programCode, programName: record.programName });
    const [yearIds, setYearIds] = useState(
        () => new Set(isCreate ? [] : record.schoolYears.map(y => y.id))
    );
    const [fieldErrors, setFieldErrors] = useState({});
    const [error, setError] = useState('');
    const [saving, setSaving] = useState(false);
    const [busy, setBusy] = useState(false);

    const set = (f) => (e) => setForm(prev => ({ ...prev, [f]: e.target.value }));
    const err = (name) => fieldErrors[name]?.[0];

    function toggleYear(id) {
        setYearIds(prev => {
            const next = new Set(prev);
            next.has(id) ? next.delete(id) : next.add(id);
            return next;
        });
    }

    function payload() {
        return { programCode: form.programCode, programName: form.programName, schoolYearIds: [...yearIds] };
    }

    async function save(e) {
        e.preventDefault();
        setError(''); setFieldErrors({}); setSaving(true);
        try {
            const saved = isCreate ? await createCurriculum(payload()) : await updateCurriculum(record.id, payload());
            onChanged(saved); onClose();
        } catch (ex) {
            setFieldErrors(ex.fieldErrors || {});
            if (!ex.fieldErrors || Object.keys(ex.fieldErrors).length === 0) setError(ex.message);
        } finally { setSaving(false); }
    }

    async function setActive() {
        setError(''); setBusy(true);
        try { const saved = await activateCurriculum(record.id); onChanged(saved); onClose(); }
        catch (ex) { setError(ex.message); } finally { setBusy(false); }
    }

    async function remove() {
        if (!window.confirm(`Delete the ${record.programCode} curriculum? This can't be undone.`)) return;
        setError(''); setBusy(true);
        try { await deleteCurriculum(record.id); onDeleted(record.id); onClose(); }
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
            <button type="submit" form="curr-form" className="btn btn-primary" disabled={saving}>
                {saving && <span className="spinner" aria-hidden="true" />}
                {saving ? 'Saving…' : isCreate ? 'Create' : 'Save changes'}
            </button>
        </>
    );

    return (
        <SetupModal title={isCreate ? 'New curriculum' : 'Edit curriculum'} onClose={onClose} footer={footer}>
            {error && <div className="alert">{error}</div>}
            <form id="curr-form" onSubmit={save} noValidate>
                <div className="field">
                    <label htmlFor="curr-code">Program code</label>
                    <input id="curr-code" value={form.programCode} onChange={set('programCode')} autoComplete="off"
                        placeholder="BSIT" style={{ textTransform: 'uppercase' }} />
                    {err('programCode') && <p className="field-error">{err('programCode')}</p>}
                </div>
                <div className="field">
                    <label htmlFor="curr-name">Program name</label>
                    <input id="curr-name" value={form.programName} onChange={set('programName')} autoComplete="off"
                        placeholder="BS Information Technology" />
                    {err('programName') && <p className="field-error">{err('programName')}</p>}
                </div>

                <div className="curr-prereq">
                    <span className="curr-prereq-label">Effective for school years</span>
                    {schoolYears.length === 0 ? (
                        <p className="curr-prereq-empty">No school years yet. Create one under Academic setup first.</p>
                    ) : (
                        <div className="curr-prereq-list">
                            {schoolYears.map(y => (
                                <label key={y.id} className="curr-prereq-item">
                                    <input type="checkbox" checked={yearIds.has(y.id)} onChange={() => toggleYear(y.id)} />
                                    <span className="curr-prereq-code">{y.name}</span>
                                    {y.isActive && <span className="chip chip-active">Active</span>}
                                </label>
                            ))}
                        </div>
                    )}
                    {err('schoolYearIds') && <p className="field-error">{err('schoolYearIds')}</p>}
                </div>
            </form>
        </SetupModal>
    );
}
