import { useState } from 'react';
import SetupModal from '../academic/SetupModal';
import { createSubject, updateSubject, deleteSubject } from './api';

const yearLevels = [1, 2, 3];
const terms = [
    { value: 'FirstSemester', label: 'First Semester' },
    { value: 'SecondSemester', label: 'Second Semester' }
];

export default function SubjectModal({ record, curriculumId, candidates, onClose, onChanged, onDeleted }) {
    const isCreate = !record;
    const [form, setForm] = useState(isCreate
        ? { code: '', title: '', units: '3', hours: '3', yearLevel: '1', term: 'FirstSemester', requiresLaboratory: false }
        : {
            code: record.code, title: record.title, units: String(record.units), hours: String(record.hours),
            yearLevel: String(record.yearLevel), term: record.term, requiresLaboratory: record.requiresLaboratory
        });
    const [prereqIds, setPrereqIds] = useState(
        isCreate ? [] : record.prerequisites.map(p => p.id)
    );
    const [fieldErrors, setFieldErrors] = useState({});
    const [error, setError] = useState('');
    const [saving, setSaving] = useState(false);
    const [busy, setBusy] = useState(false);

    const set = (f) => (e) => setForm(prev => ({ ...prev, [f]: e.target.value }));
    const err = (name) => fieldErrors[name]?.[0];

    // Candidate prerequisites: every other subject in this curriculum.
    const prereqChoices = candidates.filter(s => s.id !== record?.id);

    function togglePrereq(id) {
        setPrereqIds(prev => prev.includes(id) ? prev.filter(x => x !== id) : [...prev, id]);
    }

    function payload() {
        return {
            curriculumId,
            code: form.code,
            title: form.title,
            units: form.units === '' ? null : Number(form.units),
            hours: form.hours === '' ? null : Number(form.hours),
            yearLevel: form.yearLevel === '' ? null : Number(form.yearLevel),
            term: form.term,
            requiresLaboratory: form.requiresLaboratory,
            prerequisiteSubjectIds: prereqIds
        };
    }

    async function save(e) {
        e.preventDefault();
        setError(''); setFieldErrors({}); setSaving(true);
        try {
            if (isCreate) await createSubject(payload());
            else await updateSubject(record.id, payload());
            onChanged(); onClose();
        } catch (ex) {
            setFieldErrors(ex.fieldErrors || {});
            if (!ex.fieldErrors || Object.keys(ex.fieldErrors).length === 0) setError(ex.message);
        } finally { setSaving(false); }
    }

    async function remove() {
        if (!window.confirm(`Delete subject ${record.code}? This can't be undone.`)) return;
        setError(''); setBusy(true);
        try { await deleteSubject(record.id); onDeleted(); onClose(); }
        catch (ex) { setError(ex.message); setBusy(false); }
    }

    const footer = (
        <>
            {!isCreate && (
                <button type="button" className="btn btn-danger setup-foot-spacer" disabled={busy} onClick={remove}>
                    Delete
                </button>
            )}
            <button type="button" className="btn btn-ghost" onClick={onClose}>Cancel</button>
            <button type="submit" form="subj-form" className="btn btn-primary" disabled={saving}>
                {saving && <span className="spinner" aria-hidden="true" />}
                {saving ? 'Saving…' : isCreate ? 'Add subject' : 'Save changes'}
            </button>
        </>
    );

    return (
        <SetupModal title={isCreate ? 'New subject' : 'Edit subject'} onClose={onClose} footer={footer}>
            {error && <div className="alert">{error}</div>}
            <form id="subj-form" onSubmit={save} noValidate>
                <div className="field-row">
                    <div className="field" style={{ flex: '0 0 34%' }}>
                        <label htmlFor="subj-code">Code</label>
                        <input id="subj-code" value={form.code} onChange={set('code')} autoComplete="off"
                            placeholder="CS101" style={{ textTransform: 'uppercase' }} />
                        {err('code') && <p className="field-error">{err('code')}</p>}
                    </div>
                    <div className="field">
                        <label htmlFor="subj-title">Title</label>
                        <input id="subj-title" value={form.title} onChange={set('title')} autoComplete="off"
                            placeholder="Introduction to Computing" />
                        {err('title') && <p className="field-error">{err('title')}</p>}
                    </div>
                </div>
                <div className="field-row">
                    <div className="field">
                        <label htmlFor="subj-units">Units</label>
                        <input id="subj-units" type="number" min="1" max="20" value={form.units} onChange={set('units')} />
                        {err('units') && <p className="field-error">{err('units')}</p>}
                    </div>
                    <div className="field">
                        <label htmlFor="subj-hours">Weekly hours</label>
                        <input id="subj-hours" type="number" min="1" max="40" value={form.hours} onChange={set('hours')} />
                        {err('hours') && <p className="field-error">{err('hours')}</p>}
                    </div>
                    <div className="field">
                        <label htmlFor="subj-year">Year level</label>
                        <select id="subj-year" value={form.yearLevel} onChange={set('yearLevel')}>
                            {yearLevels.map(y => <option key={y} value={y}>Year {y}</option>)}
                        </select>
                        {err('yearLevel') && <p className="field-error">{err('yearLevel')}</p>}
                    </div>
                    <div className="field">
                        <label htmlFor="subj-term">Term</label>
                        <select id="subj-term" value={form.term} onChange={set('term')}>
                            {terms.map(t => <option key={t.value} value={t.value}>{t.label}</option>)}
                        </select>
                        {err('term') && <p className="field-error">{err('term')}</p>}
                    </div>
                </div>
                <label className="setup-checkbox">
                    <input type="checkbox" checked={form.requiresLaboratory}
                        onChange={e => setForm(prev => ({ ...prev, requiresLaboratory: e.target.checked }))} />
                    Requires a laboratory room
                </label>

                <div className="curr-prereq">
                    <span className="curr-prereq-label">Prerequisites</span>
                    {prereqChoices.length === 0 ? (
                        <p className="curr-prereq-empty">No other subjects in this curriculum yet.</p>
                    ) : (
                        <div className="curr-prereq-list">
                            {prereqChoices.map(s => (
                                <label key={s.id} className="curr-prereq-item">
                                    <input type="checkbox" checked={prereqIds.includes(s.id)} onChange={() => togglePrereq(s.id)} />
                                    <span className="curr-prereq-code">{s.code}</span>
                                    <span className="curr-prereq-title">{s.title}</span>
                                </label>
                            ))}
                        </div>
                    )}
                </div>
            </form>
        </SetupModal>
    );
}
