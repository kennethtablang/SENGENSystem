import { useState } from 'react';
import SetupModal from '../academic/SetupModal';
import { createCurriculum, updateCurriculum, activateCurriculum, archiveCurriculum, restoreCurriculum } from './api';
import { notifySuccess, notifyError } from '../shell/notify';
import { confirmAction } from '../shell/confirm';

export default function CurriculumModal({ record, schoolYears, onClose, onChanged }) {
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
            notifySuccess(isCreate ? `${saved.programCode} curriculum created.` : `${saved.programCode} curriculum updated.`);
            onChanged(saved); onClose();
        } catch (ex) {
            setFieldErrors(ex.fieldErrors || {});
            if (!ex.fieldErrors || Object.keys(ex.fieldErrors).length === 0) setError(ex.message);
            notifyError(ex.message);
        } finally { setSaving(false); }
    }

    async function setActive() {
        setError(''); setBusy(true);
        try { const saved = await activateCurriculum(record.id); notifySuccess(`${saved.programCode} is now the active curriculum for its program.`); onChanged(saved); onClose(); }
        catch (ex) { setError(ex.message); notifyError(ex.message); } finally { setBusy(false); }
    }

    // Program-change path: retire the catalog instead of deleting it, keeping its subjects and history.
    async function archive() {
        const ok = await confirmAction({
            title: `Archive the ${record.programCode} curriculum?`,
            message: 'It leaves the active catalog and can no longer be offered, but its subjects, prerequisites, and history stay intact. You can restore it any time.',
            confirmLabel: 'Archive'
        });
        if (!ok) return;
        setError(''); setBusy(true);
        try { const saved = await archiveCurriculum(record.id); notifySuccess(`${record.programCode} curriculum archived.`); onChanged(saved); onClose(); }
        catch (ex) { setError(ex.message); notifyError(ex.message); setBusy(false); }
    }

    async function restore() {
        setError(''); setBusy(true);
        try { const saved = await restoreCurriculum(record.id); notifySuccess(`${record.programCode} curriculum restored.`); onChanged(saved); onClose(); }
        catch (ex) { setError(ex.message); notifyError(ex.message); setBusy(false); }
    }

    const footer = (
        <>
            {!isCreate && (
                <span className="setup-foot-spacer" style={{ display: 'inline-flex', gap: '0.5rem' }}>
                    {record.isArchived ? (
                        <button type="button" className="btn btn-ghost" disabled={busy} onClick={restore}>
                            Restore
                        </button>
                    ) : (
                        <button type="button" className="btn btn-ghost" disabled={busy} onClick={archive}>
                            Archive
                        </button>
                    )}
                </span>
            )}
            {!isCreate && !record.isArchived && !record.isActive && (
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
            {!isCreate && record.isArchived && (
                <div className="alert alert-success" style={{ background: 'var(--sti-yellow-dim)', borderColor: 'var(--sti-yellow)', color: 'var(--text-1)' }}>
                    This curriculum is archived{record.archiveReason ? ` — ${record.archiveReason}` : ''}. It stays out of the
                    active catalog until restored.
                </div>
            )}
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
