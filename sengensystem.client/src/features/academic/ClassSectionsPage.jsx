import { useEffect, useMemo, useRef, useState } from 'react';
import SetupModal from './SetupModal';
import { notifySuccess, notifyError } from '../shell/notify';
import { confirmDelete } from '../shell/confirm';
import { listClassSections, createClassSection, updateClassSection, deleteClassSection } from './api';
import './academic.css';

// STI's courses run 2–3 years (HRA 3, HRS 2, ITP 2), so a cohort can sit at any of these.
const YEARS = [1, 2, 3];

function ClassSectionModal({ record, semesters, programs, curricula, defaultSemesterId, defaultProgram, onClose, onChanged }) {
    const isCreate = !record;
    const [form, setForm] = useState(isCreate
        ? { semesterId: defaultSemesterId || '', programCode: defaultProgram || '', yearLevel: '1', sectionName: '', curriculumId: '' }
        : { semesterId: record.semesterId, programCode: record.programCode, yearLevel: String(record.yearLevel), sectionName: record.sectionName, curriculumId: record.curriculumId || '' });
    const [fieldErrors, setFieldErrors] = useState({});
    const [error, setError] = useState('');
    const [saving, setSaving] = useState(false);
    const [busy, setBusy] = useState(false);

    const set = (f) => (e) => setForm(prev => ({ ...prev, [f]: e.target.value }));
    const err = (name) => fieldErrors[name]?.[0];

    // Only the chosen program's curricula are selectable — a cohort follows its own program's catalog.
    const programCurricula = useMemo(
        () => (curricula || []).filter(c => c.programCode === form.programCode),
        [curricula, form.programCode]);

    // When the program changes, keep the curriculum consistent: drop a selection that no longer
    // belongs, and default to the program's active catalog so the common case needs no extra click.
    const lastProgram = useRef(form.programCode);
    useEffect(() => {
        if (lastProgram.current === form.programCode) return;
        lastProgram.current = form.programCode;
        setForm(prev => {
            const stillValid = programCurricula.some(c => c.id === prev.curriculumId);
            if (stillValid) return prev;
            const active = programCurricula.find(c => c.isActive && !c.isArchived);
            return { ...prev, curriculumId: active ? active.id : '' };
        });
    }, [form.programCode, programCurricula]);

    function payload() {
        return {
            semesterId: form.semesterId || null,
            programCode: form.programCode || null,
            yearLevel: form.yearLevel === '' ? null : Number(form.yearLevel),
            sectionName: form.sectionName,
            curriculumId: form.curriculumId || null
        };
    }

    async function save(e) {
        e.preventDefault();
        setError(''); setFieldErrors({}); setSaving(true);
        try {
            if (isCreate) await createClassSection(payload());
            else await updateClassSection(record.id, payload());
            notifySuccess(isCreate ? 'Class section created.' : 'Class section updated.');
            onChanged(); onClose();
        } catch (ex) {
            notifyError(ex.message);
            setFieldErrors(ex.fieldErrors || {});
            if (!ex.fieldErrors || Object.keys(ex.fieldErrors).length === 0) setError(ex.message);
        } finally { setSaving(false); }
    }

    async function remove() {
        if (!(await confirmDelete(`class “${record.displayName}”`))) return;
        setError(''); setBusy(true);
        try { await deleteClassSection(record.id); notifySuccess(`Class “${record.displayName}” deleted.`); onChanged(); onClose(); }
        catch (ex) { setError(ex.message); notifyError(ex.message); setBusy(false); }
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
                    <label htmlFor="cs-curriculum">Curriculum</label>
                    <select id="cs-curriculum" value={form.curriculumId} onChange={set('curriculumId')}
                        disabled={!form.programCode}>
                        <option value="">{form.programCode ? 'Program active curriculum' : 'Choose a course first…'}</option>
                        {programCurricula.map(c => <option key={c.id} value={c.id}>{c.label}</option>)}
                    </select>
                    <p className="field-hint">
                        Which curriculum version this cohort follows — pick the older catalog for returning
                        year levels when the program has switched curricula.
                    </p>
                    {err('curriculumId') && <p className="field-error">{err('curriculumId')}</p>}
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
    const [curricula, setCurricula] = useState([]);
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
                setCurricula(data.curricula || []);
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

    // Program code → name, for the card headers.
    const programName = useMemo(
        () => new Map(programs.map(p => [p.code, p.name])),
        [programs]);

    // Separate the flat list into one card per course so a mixed listing is easy to scan; the
    // sections stay listed within each card, sorted by year then section.
    const grouped = useMemo(() => {
        const byProgram = new Map();
        for (const c of rows) {
            if (!byProgram.has(c.programCode)) byProgram.set(c.programCode, []);
            byProgram.get(c.programCode).push(c);
        }
        for (const list of byProgram.values()) {
            list.sort((a, b) => a.yearLevel - b.yearLevel
                || a.sectionName.localeCompare(b.sectionName, undefined, { numeric: true }));
        }
        return [...byProgram.entries()].sort((a, b) => a[0].localeCompare(b[0]));
    }, [rows]);

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
                <div className="cs-cards">
                    {grouped.map(([code, list]) => (
                        <section key={code} className="card cs-card">
                            <header className="cs-card-head">
                                <div>
                                    <h3>{code}</h3>
                                    <span className="cs-card-sub">{programName.get(code) || 'Course'}</span>
                                </div>
                                <span className="chip chip-muted">
                                    {list.length} section{list.length === 1 ? '' : 's'}
                                </span>
                            </header>
                            <table className="setup-table cs-card-table">
                                <thead>
                                    <tr>
                                        <th>Year</th>
                                        <th>Section</th>
                                        <th>Cohort</th>
                                        <th>Curriculum</th>
                                    </tr>
                                </thead>
                                <tbody>
                                    {list.map(c => (
                                        <tr key={c.id} className="setup-row" onClick={() => setModal(c)}>
                                            <td className="setup-num">Year {c.yearLevel}</td>
                                            <td><strong>{c.sectionName}</strong></td>
                                            <td className="setup-muted">{c.displayName}</td>
                                            <td>
                                                {c.curriculumLabel
                                                    ? <span className="chip chip-blue" title={c.curriculumLabel}>{c.curriculumLabel}</span>
                                                    : <span className="chip chip-yellow" title="No curriculum set — assign one so this cohort schedules correctly.">Not set</span>}
                                            </td>
                                        </tr>
                                    ))}
                                </tbody>
                            </table>
                        </section>
                    ))}
                </div>
            )}

            {modal && (
                <ClassSectionModal
                    record={modal.id ? modal : null}
                    semesters={semesters}
                    programs={programs}
                    curricula={curricula}
                    defaultSemesterId={semesterId}
                    defaultProgram={programFilter !== 'All' ? programFilter : ''}
                    onClose={() => setModal(null)}
                    onChanged={refresh}
                />
            )}
        </div>
    );
}
