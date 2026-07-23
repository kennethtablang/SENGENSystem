import { useEffect, useMemo, useState } from 'react';
import SetupModal from '../academic/SetupModal';
import { getFacultySubjects, saveFacultyLoad } from './api';
import { notifySuccess, notifyError } from '../shell/notify';

// A load row is a (subject × class section) pair; this is its stable identity.
const keyOf = (r) => `${r.subjectId}|${r.classSectionId}`;

export default function FacultyAssignModal({ faculty, semesterId, onClose, onSaved }) {
    const [data, setData] = useState(null); // { maxLoadUnits, rows: [...] }
    const [selected, setSelected] = useState(() => new Set());
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState('');
    const [saving, setSaving] = useState(false);

    const [search, setSearch] = useState('');
    const [course, setCourse] = useState('All');
    const [section, setSection] = useState('All');
    const [type, setType] = useState('All');
    // FR-FAC-01: focus the list on subjects no faculty holds yet, so the Head can see at a
    // glance what still needs a teacher for the semester.
    const [unassignedOnly, setUnassignedOnly] = useState(false);

    useEffect(() => {
        let active = true;
        (async () => {
            setLoading(true); setError('');
            try {
                const d = await getFacultySubjects(faculty.facultyProfileId, semesterId);
                if (!active) return;
                setData(d);
                setSelected(new Set(d.rows.filter(r => r.isAssigned).map(keyOf)));
            } catch (ex) { if (active) setError(ex.message); }
            finally { if (active) setLoading(false); }
        })();
        return () => { active = false; };
    }, [faculty.facultyProfileId, semesterId]);

    const rows = data?.rows ?? [];
    const maxUnits = data?.maxLoadUnits ?? faculty.maxLoadUnits ?? 0;

    const courses = useMemo(() => ['All', ...[...new Set(rows.map(r => r.course))].sort()], [rows]);
    const sections = useMemo(
        () => ['All', ...[...new Set(rows.map(r => `${r.course} ${r.yearLevel}-${r.section}`))].sort()],
        [rows]);

    const visible = useMemo(() => {
        const q = search.trim().toLowerCase();
        return rows.filter(r =>
            (course === 'All' || r.course === course)
            && (section === 'All' || `${r.course} ${r.yearLevel}-${r.section}` === section)
            && (type === 'All' || r.type === type)
            // "Unassigned" = no faculty holds this subject×section yet (available to pick up).
            && (!unassignedOnly || !r.assignedToProfileId)
            && (q === '' || r.code.toLowerCase().includes(q) || r.title.toLowerCase().includes(q)));
    }, [rows, search, course, section, type, unassignedOnly]);

    const unassignedCount = useMemo(() => rows.filter(r => !r.assignedToProfileId).length, [rows]);

    const totalUnits = useMemo(
        () => rows.filter(r => selected.has(keyOf(r))).reduce((sum, r) => sum + r.units, 0),
        [rows, selected]);
    const over = totalUnits > maxUnits;

    function toggle(key) {
        setSelected(prev => {
            const next = new Set(prev);
            next.has(key) ? next.delete(key) : next.add(key);
            return next;
        });
    }

    // A (subject, class section) pair held by *another* member can't be reassigned here.
    const takenByOther = (r) =>
        r.assignedToProfileId && r.assignedToProfileId !== faculty.facultyProfileId;

    async function save() {
        setError(''); setSaving(true);
        try {
            const items = rows.filter(r => selected.has(keyOf(r)))
                .map(r => ({ subjectId: r.subjectId, classSectionId: r.classSectionId }));
            const updated = await saveFacultyLoad(faculty.facultyProfileId, semesterId, items);
            notifySuccess(`Load of ${faculty.name} saved — ${updated.assignedUnits}/${updated.maxLoadUnits} units.`);
            onSaved(updated); onClose();
        } catch (ex) { setError(ex.message); notifyError(ex.message); }
        finally { setSaving(false); }
    }

    const footer = (
        <>
            <div className={`fl-total setup-foot-spacer${over ? ' is-over' : ''}`}>
                <span className="fl-total-label">Total load</span>
                <span className="fl-total-value">{totalUnits}<span className="fl-total-max"> / {maxUnits} units</span></span>
                <span className="fl-total-count">{selected.size} class{selected.size === 1 ? '' : 'es'}</span>
            </div>
            <button type="button" className="btn btn-ghost" onClick={onClose}>Cancel</button>
            <button type="button" className="btn btn-primary" disabled={saving || loading || over} onClick={save}>
                {saving && <span className="spinner" aria-hidden="true" />}
                {saving ? 'Saving…' : 'Save Assignments'}
            </button>
        </>
    );

    return (
        <SetupModal title={`Assign load — ${faculty.name}`} onClose={onClose} footer={footer} className="fl-modal-wide">
            {error && <div className="alert">{error}</div>}
            {over && !error && (
                <div className="alert alert-warn">
                    This load is {totalUnits} units, over the {maxUnits}-unit ceiling. Remove some classes to save.
                </div>
            )}

            <div className="fl-filters">
                <input type="search" className="fl-search" placeholder="Search code or title"
                    value={search} onChange={e => setSearch(e.target.value)} />
                <label className="fl-filter">
                    <span>Course</span>
                    <select value={course} onChange={e => setCourse(e.target.value)}>
                        {courses.map(c => <option key={c} value={c}>{c === 'All' ? 'All' : c}</option>)}
                    </select>
                </label>
                <label className="fl-filter">
                    <span>Class</span>
                    <select value={section} onChange={e => setSection(e.target.value)}>
                        {sections.map(s => <option key={s} value={s}>{s === 'All' ? 'All' : s}</option>)}
                    </select>
                </label>
                <label className="fl-filter">
                    <span>Type</span>
                    <select value={type} onChange={e => setType(e.target.value)}>
                        {['All', 'Lecture only', 'Laboratory only', 'Lecture–Laboratory']
                            .map(t => <option key={t} value={t}>{t}</option>)}
                    </select>
                </label>
                <label className="fl-filter-check" title="Show only subjects no faculty holds yet">
                    <input
                        type="checkbox"
                        checked={unassignedOnly}
                        onChange={e => setUnassignedOnly(e.target.checked)}
                    />
                    <span>Unassigned only</span>
                    <span className="fl-filter-badge">{unassignedCount}</span>
                </label>
            </div>

            {loading ? (
                <p className="fl-empty">Loading classes…</p>
            ) : rows.length === 0 ? (
                <p className="fl-empty">No class sections for this semester yet. Create classes under Academic setup → Class sections first.</p>
            ) : visible.length === 0 ? (
                <p className="fl-empty">
                    {unassignedOnly
                        ? 'Every subject that matches these filters is already assigned to a faculty member.'
                        : 'No classes match these filters.'}
                </p>
            ) : (
                <div className="fl-subject-list">
                    <div className="fl-subject-head">
                        <span />
                        <span>Subject</span>
                        <span className="fl-col-course">Class</span>
                        <span className="fl-col-year">Year</span>
                        <span className="fl-col-units">Units</span>
                        <span className="fl-col-type">Type</span>
                    </div>
                    {visible.map(r => {
                        const key = keyOf(r);
                        const checked = selected.has(key);
                        const taken = takenByOther(r);
                        const rowClass = taken ? ' is-taken' : checked ? ' is-on' : '';
                        return (
                            <label
                                key={key}
                                className={`fl-subject-row${rowClass}`}
                                title={taken ? `Assigned to ${r.assignedToName} — ${r.course} ${r.yearLevel}-${r.section}` : undefined}
                            >
                                <input
                                    type="checkbox"
                                    checked={checked && !taken}
                                    disabled={taken}
                                    onChange={() => toggle(key)}
                                />
                                <span className="fl-subject-main">
                                    <span className="fl-subject-code">{r.code}</span>
                                    <span className="fl-subject-title">{r.title}</span>
                                    {taken && (
                                        <span className="fl-assigned-to">
                                            <span className="fl-assigned-badge">Assigned</span>
                                            {r.assignedToName} · {r.course} {r.yearLevel}-{r.section}
                                        </span>
                                    )}
                                </span>
                                <span className="fl-col-course">{r.course} {r.yearLevel}-{r.section}</span>
                                <span className="fl-col-year">Y{r.yearLevel}</span>
                                <span className="fl-col-units">{r.units}u</span>
                                <span className="fl-col-type">
                                    <span className={r.type === 'Lecture only' ? 'chip chip-muted' : 'chip chip-lab'}>{r.type}</span>
                                </span>
                            </label>
                        );
                    })}
                </div>
            )}
        </SetupModal>
    );
}
