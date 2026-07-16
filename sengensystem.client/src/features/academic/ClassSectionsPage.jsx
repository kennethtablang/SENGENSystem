import { useEffect, useState } from 'react';
import SetupModal from './SetupModal';
import { listClassSections, createClassSection, updateClassSection, deleteClassSection } from './api';
import './academic.css';

const YEARS = [3];

function ClassSectionModal({ record, semesters, programs, defaultSemesterId, defaultProgram, onClose, onChanged }) {
    const isCreate = !record;
    const [form, setForm] = useState(isCreate
        ? { semesterId: defaultSemesterId || '', programCode: defaultProgram || '', yearLevel: '3', sectionName: '' }
        : { semesterId: record.semesterId, programCode: record.programCode, yearLevel: String(record.yearLevel), sectionName: record.sectionName });
    const [fieldErrors, setFieldErrors] = useState({});
    const [error, setError] = useState('');
    const [saving, setSaving] = useState(false);
    const [busy, setBusy] = useState(false);

    const set = (f) => (e) => setForm(prev => ({ ...prev, [f]: e.target.value }));
    const err = (name) => fieldErrors[name]?.[0];

    function payload() {
        return {
            semesterId: form.semesterId || null,
            programCode: form.programCode || null,
            yearLevel: form.yearLevel === '' ? null : Number(form.yearLevel),
            sectionName: form.sectionName
        };
    }

    async function save(e) {
        e.preventDefault();
        setError(''); setFieldErrors({}); setSaving(true);
        try {
            if (isCreate) await createClassSection(payload());
            else await updateClassSection(record.id, payload());
            onChanged(); onClose();
        } catch (ex) {
            setFieldErrors(ex.fieldErrors || {});
            if (!ex.fieldErrors || Object.keys(ex.fieldErrors).length === 0) setError(ex.message);
        } finally { setSaving(false); }
    }

    async function remove() {
        if (!window.confirm(`Delete class “${record.displayName}”? This can't be undone.`)) return;
        setError(''); setBusy(true);
        try { await deleteClassSection(record.id); onChanged(); onClose(); }
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
            <button type="submit" form="class-section-form" className="btn btn-primary" disabled={saving}>
                {saving && <span className="spinner" aria-hidden="true" />}
                {saving ? 'Saving…' : isCreate ? 'Create' : 'Save changes'}
            </button>
        </>
    );

    return (
        <SetupModal title={isCreate ? 'New class' : 'Edit class'} onClose={onClose} footer={footer}>
            {error && <div className="alert">{error}</div>}
            <form id="class-section-form" onSubmit={save} noValidate>
                <div className="field">
                    <label htmlFor="cs-semester">Semester / term</label>
                    <select id="cs-semester" value={form.semesterId} onChange={set('semesterId')}>
                        <option value="">Select a semester…</option>
                        {semesters.map(s => (
                            <option key={s.id} value={s.id}>{s.name}{s.isActive ? ' (active)' : ''}</option>
                        ))}
                    </select>
                    {err('semesterId') && <p className="field-error">{err('semesterId')}</p>}
                </div>
                <div className="field">
                    <label htmlFor="cs-program">Course</label>
                    <select id="cs-program" value={form.programCode} onChange={set('programCode')}>
                        <option value="">Select a course…</option>
                        {programs.map(p => <option key={p.code} value={p.code}>{p.code} — {p.name}</option>)}
                    </select>
                    {err('programCode') && <p className="field-error">{err('programCode')}</p>}
                </div>
                <div className="field">
                    <label htmlFor="cs-year">Year level</label>
                    <select id="cs-year" value={form.yearLevel} onChange={set('yearLevel')}>
                        {YEARS.map(y => <option key={y} value={y}>Year {y}</option>)}
                    </select>
                    {err('yearLevel') && <p className="field-error">{err('yearLevel')}</p>}
                </div>
                <div className="field">
                    <label htmlFor="cs-section">Section</label>
                    <input id="cs-section" value={form.sectionName} onChange={set('sectionName')}
                        autoComplete="off" placeholder="A" maxLength={20} />
                    {err('sectionName') && <p className="field-error">{err('sectionName')}</p>}
                </div>
            </form>
        </SetupModal>
    );
}

export default function ClassSectionsPage() {
    const [rows, setRows] = useState([]);
    const [semesters, setSemesters] = useState([]);
    const [semesterId, setSemesterId] = useState('');
    const [programs, setPrograms] = useState([]);
    const [programFilter, setProgramFilter] = useState('All');
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState(null);
    const [reload, setReload] = useState(0);
    const [modal, setModal] = useState(null);

    useEffect(() => {
        let active = true;
        (async () => {
            setLoading(true); setError(null);
            try {
                const data = await listClassSections(
                    semesterId || undefined,
                    programFilter === 'All' ? undefined : programFilter);
                if (!active) return;
                setSemesters(data.semesters);
                setPrograms(data.programs);
                setRows(data.classSections);
                // Adopt the resolved (active) semester on first load.
                if (!semesterId && data.semesterId) setSemesterId(data.semesterId);
            } catch (err) { if (active) setError(err.message); }
            finally { if (active) setLoading(false); }
        })();
        return () => { active = false; };
    }, [semesterId, programFilter, reload]);

    const refresh = () => setReload(r => r + 1);
    const canCreate = programs.length > 0 && semesters.length > 0;

    return (
        <div className="setup-page">
            <header className="setup-head">
                <div>
                    <h2>Class sections</h2>
                    <p className="setup-sub">
                        Define student class blocks for a semester — a course at a year level, split into
                        named sections (e.g. BSCS · Year 3 · “A”). Classes are created afresh each term.
                    </p>
                </div>
                <div className="setup-controls">
                    <label className="setup-filter">
                        <span>Semester</span>
                        <select value={semesterId} onChange={e => setSemesterId(e.target.value)}
                            disabled={semesters.length === 0}>
                            {semesters.length === 0 && <option value="">No semesters</option>}
                            {semesters.map(s => (
                                <option key={s.id} value={s.id}>{s.name}{s.isActive ? ' (active)' : ''}</option>
                            ))}
                        </select>
                    </label>
                    <label className="setup-filter">
                        <span>Course</span>
                        <select value={programFilter} onChange={e => setProgramFilter(e.target.value)}>
                            <option value="All">All</option>
                            {programs.map(p => <option key={p.code} value={p.code}>{p.code}</option>)}
                        </select>
                    </label>
                    <button className="btn btn-primary btn-sm" type="button"
                        disabled={!canCreate}
                        title={!canCreate ? 'Create a semester and a curriculum/program first' : undefined}
                        onClick={() => setModal({})}>
                        New class
                    </button>
                </div>
            </header>

            {error && <div className="alert">{error}</div>}

            {loading ? (
                <p className="setup-empty">Loading…</p>
            ) : semesters.length === 0 ? (
                <p className="setup-empty">No semesters yet. Create a semester under Academic setup first.</p>
            ) : programs.length === 0 ? (
                <p className="setup-empty">No programs yet. Create a curriculum under Subjects &amp; curriculum first.</p>
            ) : rows.length === 0 ? (
                <p className="setup-empty">No classes{programFilter !== 'All' ? ' for this course' : ''} this semester.</p>
            ) : (
                <div className="card setup-table-wrap">
                    <table className="setup-table">
                        <thead>
                            <tr>
                                <th>Class</th>
                                <th>Semester / term</th>
                                <th>Course</th>
                                <th>Year</th>
                                <th>Section</th>
                            </tr>
                        </thead>
                        <tbody>
                            {rows.map(c => (
                                <tr key={c.id} className="setup-row" onClick={() => setModal(c)}>
                                    <td><strong>{c.displayName}</strong></td>
                                    <td className="setup-muted">{c.semesterName || '—'}</td>
                                    <td className="setup-muted">{c.programCode}</td>
                                    <td className="setup-num">Year {c.yearLevel}</td>
                                    <td>{c.sectionName}</td>
                                </tr>
                            ))}
                        </tbody>
                    </table>
                </div>
            )}

            {modal && (
                <ClassSectionModal
                    record={modal.id ? modal : null}
                    semesters={semesters}
                    programs={programs}
                    defaultSemesterId={semesterId}
                    defaultProgram={programFilter !== 'All' ? programFilter : ''}
                    onClose={() => setModal(null)}
                    onChanged={refresh}
                />
            )}
        </div>
    );
}
