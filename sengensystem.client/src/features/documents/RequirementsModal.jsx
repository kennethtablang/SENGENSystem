import { useEffect, useCallback, useState } from 'react';
import SetupModal from '../academic/SetupModal';
import { listRequirements, createRequirement, updateRequirement, archiveRequirement } from './api';
import { notifySuccess, notifyError } from '../shell/notify';
import { confirmAction } from '../shell/confirm';
import { programOptions, studentTypeOptions } from '../registration/options';

/* FR-DOC-01: manage the configurable admission-requirement catalog. Staff add, rename, and archive
   requirements and tick which programs (courses) and student types each applies to — so, e.g., ITP
   enrollees are not asked for the health papers only HRS/HRA need, and a transferee is not asked
   for the Form 138 only a new enrollee's high school can issue. Each requirement can also gate
   pre-authorization (FR-PRE-02) and accept a certificate of grades in place of a photocopy.
   New SIS submissions seed their checklist from the active requirements that match. */

const emptyForm = {
    name: '',
    description: '',
    programs: programOptions.map(p => p.value),
    studentTypes: studentTypeOptions.map(t => t.value),
    isRequiredForAuthorization: false,
    acceptsCertificateOfGrades: false
};

/** "New students only" / "Transferees only" / "All student types" — for the catalog list chips. */
function studentTypeSummary(types) {
    const set = types ?? studentTypeOptions.map(t => t.value);
    if (set.length >= studentTypeOptions.length) return 'All student types';
    if (set.length === 0) return 'No student types';
    return set.includes('Transferee') ? 'Transferees only' : 'New students only';
}

export default function RequirementsModal({ onClose }) {
    const [requirements, setRequirements] = useState(null);
    const [error, setError] = useState('');
    const [editing, setEditing] = useState(null); // 'new' | requirement id | null
    const [form, setForm] = useState(emptyForm);
    const [busy, setBusy] = useState(false);

    const reload = useCallback(async () => {
        try {
            const data = await listRequirements();
            setRequirements(data.requirements);
            setError('');
        } catch (err) {
            setError(err.message);
        }
    }, []);

    useEffect(() => {
        let active = true;
        (async () => {
            try {
                const data = await listRequirements();
                if (active) setRequirements(data.requirements);
            } catch (err) {
                if (active) setError(err.message);
            }
        })();
        return () => { active = false; };
    }, []);

    function startAdd() {
        setForm(emptyForm);
        setEditing('new');
    }

    function startEdit(req) {
        setForm({
            name: req.name,
            description: req.description ?? '',
            programs: [...req.programs],
            studentTypes: [...(req.studentTypes ?? studentTypeOptions.map(t => t.value))],
            isRequiredForAuthorization: !!req.isRequiredForAuthorization,
            acceptsCertificateOfGrades: !!req.acceptsCertificateOfGrades
        });
        setEditing(req.id);
    }

    function toggleIn(key, value) {
        setForm(f => ({
            ...f,
            [key]: f[key].includes(value)
                ? f[key].filter(v => v !== value)
                : [...f[key], value]
        }));
    }

    async function save(e) {
        e.preventDefault();
        setBusy(true);
        setError('');
        try {
            const payload = {
                name: form.name.trim(),
                description: form.description.trim() || null,
                programs: form.programs,
                studentTypes: form.studentTypes,
                isRequiredForAuthorization: form.isRequiredForAuthorization,
                acceptsCertificateOfGrades: form.acceptsCertificateOfGrades
            };
            if (editing === 'new') {
                await createRequirement(payload);
                notifySuccess(`Added requirement "${payload.name}".`);
            } else {
                await updateRequirement(editing, payload);
                notifySuccess(`Updated requirement "${payload.name}".`);
            }
            setEditing(null);
            await reload();
        } catch (err) {
            const text = err.fieldErrors?.name?.[0]
                || err.fieldErrors?.programs?.[0]
                || err.fieldErrors?.studentTypes?.[0]
                || err.message;
            setError(text);
            notifyError(text);
        } finally {
            setBusy(false);
        }
    }

    async function archive(req) {
        const ok = await confirmAction({
            title: `Archive "${req.name}"?`,
            message: 'Archived requirements are no longer added to new checklists. Students already '
                + 'asked for it keep their existing entry, and you can re-add a fresh requirement later.',
            confirmLabel: 'Archive'
        });
        if (!ok) return;
        try {
            await archiveRequirement(req.id);
            notifySuccess(`Archived "${req.name}".`);
            await reload();
        } catch (err) {
            notifyError(err.message);
        }
    }

    const footer = (
        <>
            <span className="setup-foot-spacer" />
            <button type="button" className="btn btn-ghost" onClick={onClose}>Close</button>
            {!editing && (
                <button type="button" className="btn btn-primary" onClick={startAdd}>
                    Add requirement
                </button>
            )}
        </>
    );

    return (
        <SetupModal title="Admission requirements" onClose={onClose} footer={footer} className="req-modal">
            {error && <div className="alert">{error}</div>}

            <p className="req-lead">
                These are the papers the Admission Office collects. Choose which programs and student types
                each applies to — a student is only asked for the requirements their course and route into
                the school actually call for — and which of them must be on file before they can be cleared
                for enlistment.
            </p>

            {editing ? (
                <form className="req-form" onSubmit={save}>
                    <h4>{editing === 'new' ? 'New requirement' : 'Edit requirement'}</h4>
                    <label className="req-field">
                        <span>Name</span>
                        <input
                            type="text" value={form.name} autoFocus required
                            placeholder="e.g. Barangay Clearance"
                            onChange={e => setForm(f => ({ ...f, name: e.target.value }))}
                        />
                    </label>
                    <label className="req-field">
                        <span>Description <em>(optional)</em></span>
                        <textarea
                            rows={2} value={form.description}
                            placeholder="A short note shown to staff."
                            onChange={e => setForm(f => ({ ...f, description: e.target.value }))}
                        />
                    </label>
                    <div className="req-field">
                        <span>Applies to programs</span>
                        <div className="req-programs">
                            {programOptions.map(p => (
                                <label key={p.value} className="req-check">
                                    <input
                                        type="checkbox"
                                        checked={form.programs.includes(p.value)}
                                        onChange={() => toggleIn('programs', p.value)}
                                    />
                                    <span>{p.value}</span>
                                </label>
                            ))}
                        </div>
                    </div>
                    <div className="req-field">
                        <span>Applies to student types</span>
                        <div className="req-programs">
                            {studentTypeOptions.map(t => (
                                <label key={t.value} className="req-check">
                                    <input
                                        type="checkbox"
                                        checked={form.studentTypes.includes(t.value)}
                                        onChange={() => toggleIn('studentTypes', t.value)}
                                    />
                                    <span>{t.label}</span>
                                </label>
                            ))}
                        </div>
                        <small className="req-hint">
                            A paper only the school a student is leaving can issue belongs to one type —
                            the report card, permanent record, and good moral to new students; the transcript
                            and certificate of transfer to transferees.
                        </small>
                    </div>
                    <div className="req-field">
                        <span>Rules</span>
                        <label className="req-check req-check-block">
                            <input
                                type="checkbox"
                                checked={form.isRequiredForAuthorization}
                                onChange={e => setForm(f => ({ ...f, isRequiredForAuthorization: e.target.checked }))}
                            />
                            <span>
                                Required before pre-authorization
                                <small>The student cannot be cleared for enlistment until this is on file. Leave off for papers that may follow.</small>
                            </span>
                        </label>
                        <label className="req-check req-check-block">
                            <input
                                type="checkbox"
                                checked={form.acceptsCertificateOfGrades}
                                onChange={e => setForm(f => ({ ...f, acceptsCertificateOfGrades: e.target.checked }))}
                            />
                            <span>
                                Accepts a certificate of grades
                                <small>Offers “Certificate of grades” instead of “Xerox copy” on the checklist — a photocopy is not accepted for this paper.</small>
                            </span>
                        </label>
                    </div>
                    <div className="req-form-actions">
                        <button type="button" className="btn btn-ghost" disabled={busy} onClick={() => setEditing(null)}>
                            Cancel
                        </button>
                        <button type="submit" className="btn btn-primary" disabled={busy}>
                            {busy ? 'Saving…' : 'Save requirement'}
                        </button>
                    </div>
                </form>
            ) : requirements === null ? (
                <p className="reg-empty">Loading…</p>
            ) : requirements.length === 0 ? (
                <p className="reg-empty">No requirements yet. Add the first one.</p>
            ) : (
                <ul className="req-list">
                    {requirements.map(req => (
                        <li key={req.id} className={`req-item${req.isActive ? '' : ' is-archived'}`}>
                            <div className="req-item-main">
                                <div className="req-item-name">
                                    {req.name}
                                    {!req.isActive && <span className="chip chip-muted">Archived</span>}
                                </div>
                                {req.description && <div className="req-item-desc">{req.description}</div>}
                                <div className="req-item-programs">
                                    {req.programs.length === 0
                                        ? <span className="chip chip-yellow">No programs</span>
                                        : req.programs.map(p => <span key={p} className="chip chip-blue">{p}</span>)}
                                    <span className="chip chip-muted">{studentTypeSummary(req.studentTypes)}</span>
                                    {req.isRequiredForAuthorization && (
                                        <span className="chip chip-yellow" title="Blocks pre-authorization until it is on file">
                                            Gates authorization
                                        </span>
                                    )}
                                    {req.acceptsCertificateOfGrades && (
                                        <span className="chip chip-muted" title="Offered instead of a xerox copy">
                                            Certificate of grades
                                        </span>
                                    )}
                                </div>
                            </div>
                            <div className="req-item-actions">
                                <button type="button" className="btn btn-sm btn-ghost" onClick={() => startEdit(req)}>
                                    Edit
                                </button>
                                {req.isActive && (
                                    <button type="button" className="btn btn-sm btn-ghost" onClick={() => archive(req)}>
                                        Archive
                                    </button>
                                )}
                            </div>
                        </li>
                    ))}
                </ul>
            )}
        </SetupModal>
    );
}
