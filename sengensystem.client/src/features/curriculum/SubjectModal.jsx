import { useState } from 'react';
import SetupModal from '../academic/SetupModal';
import { createSubject, updateSubject, archiveSubject, restoreSubject } from './api';
import { notifySuccess, notifyError } from '../shell/notify';
import { confirmAction } from '../shell/confirm';

const yearLevels = [1, 2, 3];
const terms = [
    { value: 'FirstSemester', label: 'First Semester' },
    { value: 'SecondSemester', label: 'Second Semester' }
];

// How the subject meets. A lecture-laboratory subject is scheduled as two separate meetings —
// its lecture hours in a lecture room, its laboratory hours in the laboratory it requires — so
// the two hour figures are collected separately rather than as one total.
const deliveries = [
    { value: 'LectureOnly', label: 'Lecture only' },
    { value: 'LaboratoryOnly', label: 'Laboratory only' },
    { value: 'LectureLaboratory', label: 'Lecture–Laboratory' }
];

const labKinds = [
    { value: 'ComputerLaboratory', label: 'Computer laboratory' },
    { value: 'KitchenLaboratory', label: 'Kitchen laboratory' }
];

const hasLecture = (d) => d === 'LectureOnly' || d === 'LectureLaboratory';
const hasLab = (d) => d === 'LaboratoryOnly' || d === 'LectureLaboratory';

export default function SubjectModal({ record, curriculumId, candidates, onClose, onChanged }) {
    const isCreate = !record;
    const [form, setForm] = useState(isCreate
        ? {
            code: '', title: '', units: '3', yearLevel: '1', term: 'FirstSemester',
            delivery: 'LectureOnly', lectureHours: '3', laboratoryHours: '3', labRoomKind: 'ComputerLaboratory'
        }
        : {
            code: record.code, title: record.title, units: String(record.units),
            yearLevel: String(record.yearLevel), term: record.term,
            delivery: record.delivery,
            // Keep a sensible value in the hidden field so switching delivery doesn't start blank.
            lectureHours: String(record.lectureHours || 3),
            laboratoryHours: String(record.laboratoryHours || 3),
            labRoomKind: record.labRoomKind || 'ComputerLaboratory'
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

    const num = (v) => (v === '' ? null : Number(v));

    function payload() {
        return {
            curriculumId,
            code: form.code,
            title: form.title,
            units: num(form.units),
            yearLevel: num(form.yearLevel),
            term: form.term,
            delivery: form.delivery,
            // Only the halves this delivery actually has are sent; the server zeroes the rest.
            lectureHours: hasLecture(form.delivery) ? num(form.lectureHours) : 0,
            laboratoryHours: hasLab(form.delivery) ? num(form.laboratoryHours) : 0,
            labRoomKind: hasLab(form.delivery) ? form.labRoomKind : null,
            prerequisiteSubjectIds: prereqIds
        };
    }

    const totalHours =
        (hasLecture(form.delivery) ? Number(form.lectureHours) || 0 : 0) +
        (hasLab(form.delivery) ? Number(form.laboratoryHours) || 0 : 0);

    async function save(e) {
        e.preventDefault();
        setError(''); setFieldErrors({}); setSaving(true);
        try {
            if (isCreate) await createSubject(payload());
            else await updateSubject(record.id, payload());
            notifySuccess(isCreate ? 'Subject created.' : 'Subject updated.');
            onChanged(); onClose();
        } catch (ex) {
            setFieldErrors(ex.fieldErrors || {});
            if (!ex.fieldErrors || Object.keys(ex.fieldErrors).length === 0) setError(ex.message);
            notifyError(ex.message);
        } finally { setSaving(false); }
    }

    // Subjects are archived, never deleted — retiring one keeps its sections, loads, and history.
    async function archive() {
        const ok = await confirmAction({
            title: `Archive ${record.code}?`,
            message: 'The subject leaves the active curriculum and can no longer be offered or assigned, but its sections, loads, and history stay intact. You can restore it any time.',
            confirmLabel: 'Archive'
        });
        if (!ok) return;
        setError(''); setBusy(true);
        try { await archiveSubject(record.id); notifySuccess(`Subject ${record.code} archived.`); onChanged(); onClose(); }
        catch (ex) { setError(ex.message); notifyError(ex.message); setBusy(false); }
    }

    async function restore() {
        setError(''); setBusy(true);
        try { await restoreSubject(record.id); notifySuccess(`Subject ${record.code} restored.`); onChanged(); onClose(); }
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
            <button type="button" className="btn btn-ghost" onClick={onClose}>Cancel</button>
            <button type="submit" form="subj-form" className="btn btn-primary" disabled={saving}>
                {saving && <span className="spinner" aria-hidden="true" />}
                {saving ? 'Saving…' : isCreate ? 'Add subject' : 'Save changes'}
            </button>
        </>
    );

    return (
        <SetupModal title={isCreate ? 'New subject' : 'Edit subject'} onClose={onClose} footer={footer} className="modal-lg">
            {error && <div className="alert">{error}</div>}
            {!isCreate && record.isArchived && (
                <div className="alert alert-success" style={{ background: 'var(--sti-yellow-dim)', borderColor: 'var(--sti-yellow)', color: 'var(--text-1)' }}>
                    This subject is archived{record.archiveReason ? ` — ${record.archiveReason}` : ''}. It stays out of
                    faculty-load offers and new sections until restored.
                </div>
            )}
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

                <fieldset className="curr-delivery">
                    <legend>Delivery</legend>
                    <p className="curr-delivery-hint">
                        A lecture–laboratory subject is plotted as two separate meetings: its lecture hours in a
                        lecture room and its laboratory hours in the laboratory it needs.
                    </p>
                    <div className="field-row">
                        <div className="field">
                            <label htmlFor="subj-delivery">Type</label>
                            <select id="subj-delivery" value={form.delivery} onChange={set('delivery')}>
                                {deliveries.map(d => <option key={d.value} value={d.value}>{d.label}</option>)}
                            </select>
                            {err('delivery') && <p className="field-error">{err('delivery')}</p>}
                        </div>
                        {hasLecture(form.delivery) && (
                            <div className="field">
                                <label htmlFor="subj-lec-hours">Lecture hours / week</label>
                                <input id="subj-lec-hours" type="number" min="1" max="40"
                                    value={form.lectureHours} onChange={set('lectureHours')} />
                                {err('lectureHours') && <p className="field-error">{err('lectureHours')}</p>}
                            </div>
                        )}
                        {hasLab(form.delivery) && (
                            <div className="field">
                                <label htmlFor="subj-lab-hours">Laboratory hours / week</label>
                                <input id="subj-lab-hours" type="number" min="1" max="40"
                                    value={form.laboratoryHours} onChange={set('laboratoryHours')} />
                                {err('laboratoryHours') && <p className="field-error">{err('laboratoryHours')}</p>}
                            </div>
                        )}
                    </div>
                    {hasLab(form.delivery) && (
                        <div className="field">
                            <label htmlFor="subj-lab-kind">Laboratory required</label>
                            <select id="subj-lab-kind" value={form.labRoomKind} onChange={set('labRoomKind')}>
                                {labKinds.map(k => <option key={k.value} value={k.value}>{k.label}</option>)}
                            </select>
                            {err('labRoomKind') && <p className="field-error">{err('labRoomKind')}</p>}
                        </div>
                    )}
                    <p className="curr-delivery-total">
                        Total weekly contact hours: <strong>{totalHours}h</strong>
                    </p>
                </fieldset>

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
