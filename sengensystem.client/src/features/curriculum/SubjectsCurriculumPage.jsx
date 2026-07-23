import { useEffect, useMemo, useState } from 'react';
import { listCurricula, listSubjects } from './api';
import CurriculumModal from './CurriculumModal';
import SubjectModal from './SubjectModal';
import ArchivedModal from './ArchivedModal';
import './curriculum.css';

const TERM_LABEL = { FirstSemester: 'First Semester', SecondSemester: 'Second Semester' };
const TERM_ORDER = ['FirstSemester', 'SecondSemester'];

export default function SubjectsCurriculumPage() {
    const [curricula, setCurricula] = useState([]);
    const [allSchoolYears, setAllSchoolYears] = useState([]);
    const [selectedId, setSelectedId] = useState(null);
    const [subjects, setSubjects] = useState([]);
    const [loadingC, setLoadingC] = useState(true);
    const [loadingS, setLoadingS] = useState(false);
    const [error, setError] = useState(null);
    const [currModal, setCurrModal] = useState(null); // null | {} | record
    const [subjModal, setSubjModal] = useState(null);
    const [reloadC, setReloadC] = useState(0);
    const [reloadS, setReloadS] = useState(0);
    const [archiveOpen, setArchiveOpen] = useState(false);
    const [allSubjects, setAllSubjects] = useState([]);

    // Load curricula; keep a valid selection.
    useEffect(() => {
        let active = true;
        (async () => {
            setLoadingC(true); setError(null);
            try {
                const data = await listCurricula();
                if (!active) return;
                setCurricula(data.curricula);
                setAllSchoolYears(data.schoolYears || []);
                setSelectedId(prev => {
                    if (prev && data.curricula.some(c => c.id === prev)) return prev;
                    const activeOne = data.curricula.find(c => c.isActive);
                    return activeOne?.id ?? data.curricula[0]?.id ?? null;
                });
            } catch (err) { if (active) setError(err.message); }
            finally { if (active) setLoadingC(false); }
        })();
        return () => { active = false; };
    }, [reloadC]);

    // Load subjects for the selected curriculum.
    useEffect(() => {
        let active = true;
        (async () => {
            if (!selectedId) { if (active) setSubjects([]); return; }
            setLoadingS(true);
            try { const data = await listSubjects(selectedId); if (active) setSubjects(data.subjects); }
            catch (err) { if (active) setError(err.message); }
            finally { if (active) setLoadingS(false); }
        })();
        return () => { active = false; };
    }, [selectedId, reloadS]);

    // Every subject in the system, so the archive drawer can list retired subjects across
    // curricula (not just the selected one) and the button can show a true count.
    useEffect(() => {
        let active = true;
        listSubjects()
            .then(data => { if (active) setAllSubjects(data.subjects); })
            .catch(() => { /* the archive drawer degrades to the selected curriculum */ });
        return () => { active = false; };
    }, [reloadC, reloadS]);

    const refreshCurricula = () => setReloadC(n => n + 1);
    const refreshSubjects = () => setReloadS(n => n + 1);
    const selected = curricula.find(c => c.id === selectedId) || null;

    const archivedCount = useMemo(
        () => allSubjects.filter(s => s.isArchived).length + curricula.filter(c => c.isArchived).length,
        [allSubjects, curricula]
    );
    // The sheet always shows the live curriculum; archived rows live in the drawer.
    const visibleSubjects = useMemo(() => subjects.filter(s => !s.isArchived), [subjects]);

    // Group subjects by year level, then by term (First/Second Semester), for the curriculum sheet.
    const groups = useMemo(() => {
        const byYear = new Map();
        for (const s of visibleSubjects) {
            if (!byYear.has(s.yearLevel)) byYear.set(s.yearLevel, new Map());
            const byTerm = byYear.get(s.yearLevel);
            if (!byTerm.has(s.term)) byTerm.set(s.term, []);
            byTerm.get(s.term).push(s);
        }
        return [...byYear.entries()]
            .sort((a, b) => a[0] - b[0])
            .map(([year, byTerm]) => ({
                year,
                terms: TERM_ORDER
                    .filter(t => byTerm.has(t))
                    .map(t => ({ term: t, list: [...byTerm.get(t)].sort((a, b) => a.code.localeCompare(b.code)) }))
            }));
    }, [visibleSubjects]);

    return (
        <div className="curr-page">
            <aside className="curr-rail card">
                <header className="curr-rail-head">
                    <h2>Curricula</h2>
                    <button type="button" className="curr-add" title="New curriculum" onClick={() => setCurrModal({})}>+</button>
                </header>

                {loadingC ? (
                    <p className="curr-rail-empty">Loading…</p>
                ) : curricula.length === 0 ? (
                    <p className="curr-rail-empty">No curricula yet. Create one to begin.</p>
                ) : (
                    <ul className="curr-list">
                        {curricula.map(c => (
                            <li key={c.id}>
                                <button
                                    type="button"
                                    className={`curr-item${c.id === selectedId ? ' is-selected' : ''}${c.isArchived ? ' is-archived' : ''}`}
                                    onClick={() => setSelectedId(c.id)}
                                >
                                    <span className="curr-item-top">
                                        <span className="curr-item-label">{c.label}</span>
                                        {c.isArchived
                                            ? <span className="chip chip-muted">Archived</span>
                                            : c.isActive && <span className="curr-dot" title="Active" />}
                                    </span>
                                    <span className="curr-item-name">{c.programName}</span>
                                    <span className="curr-item-years">
                                        {c.schoolYears.length > 0 ? c.schoolYears.map(y => y.name).join(', ') : 'No school years'}
                                    </span>
                                    <span className="curr-item-count">{c.subjectCount} subject{c.subjectCount === 1 ? '' : 's'}</span>
                                </button>
                            </li>
                        ))}
                    </ul>
                )}
            </aside>

            <section className="curr-main">
                {error && <div className="alert">{error}</div>}

                {!selected ? (
                    <div className="curr-blank card">
                        <h3>Subjects &amp; Curriculum</h3>
                        <p>Select a curriculum on the left, or create one, to manage its subjects.</p>
                    </div>
                ) : (
                    <>
                        <header className="curr-main-head">
                            <div>
                                <div className="curr-main-title">
                                    <h2>{selected.label}</h2>
                                    {selected.isArchived
                                        ? <span className="chip chip-muted">Archived</span>
                                        : selected.isActive
                                            ? <span className="chip chip-active">Active</span>
                                            : <span className="chip chip-muted">Inactive</span>}
                                </div>
                                <p className="curr-main-sub">{selected.programName}</p>
                                <div className="curr-main-years">
                                    {selected.schoolYears.length > 0
                                        ? selected.schoolYears.map(y => <span key={y.id} className="chip chip-blue">{y.name}</span>)
                                        : <span className="chip chip-muted">No school years — edit to set effectivity</span>}
                                </div>
                            </div>
                            <div className="curr-main-actions">
                                {archivedCount > 0 && (
                                    <button
                                        type="button"
                                        className="btn btn-ghost btn-sm"
                                        onClick={() => setArchiveOpen(true)}
                                    >
                                        Show archived ({archivedCount})
                                    </button>
                                )}
                                <button type="button" className="btn btn-ghost btn-sm" onClick={() => setCurrModal(selected)}>
                                    Edit curriculum
                                </button>
                                <button type="button" className="btn btn-primary btn-sm" onClick={() => setSubjModal({})}>
                                    New subject
                                </button>
                            </div>
                        </header>

                        {loadingS ? (
                            <p className="curr-subj-empty">Loading subjects…</p>
                        ) : subjects.length === 0 ? (
                            <p className="curr-subj-empty">No subjects in this curriculum yet. Add the first one.</p>
                        ) : (
                            <div className="curr-groups">
                                {groups.map(g => (
                                    <section key={g.year} className="curr-group">
                                        <h3 className="curr-group-head">Year {g.year}</h3>
                                        {g.terms.map(t => (
                                            <div key={t.term} className="curr-term">
                                                <h4 className="curr-term-head">
                                                    {TERM_LABEL[t.term] || t.term}
                                                    <span className="curr-term-units">
                                                        {t.list.reduce((sum, s) => sum + s.units, 0)} units · {t.list.length} subject{t.list.length === 1 ? '' : 's'}
                                                    </span>
                                                </h4>
                                                <div className="card curr-table-wrap">
                                                    <table className="curr-table">
                                                        <tbody>
                                                            {t.list.map(s => (
                                                                <tr key={s.id} className="curr-subj-row" onClick={() => setSubjModal(s)}>
                                                                    <td className="curr-subj-code">{s.code}</td>
                                                                    <td className="curr-subj-title">
                                                                        {s.title}
                                                                        {s.prerequisites.length > 0 && (
                                                                            <span className="curr-prereq-chips">
                                                                                {s.prerequisites.map(p => (
                                                                                    <span key={p.id} className="chip chip-muted" title={p.title}>{p.code}</span>
                                                                                ))}
                                                                            </span>
                                                                        )}
                                                                    </td>
                                                                    <td className="curr-subj-units">
                                                                        {s.units}u · {s.hours}h
                                                                        {s.delivery === 'LectureLaboratory' && (
                                                                            <span className="curr-subj-split">
                                                                                {s.lectureHours}h lec + {s.laboratoryHours}h lab
                                                                            </span>
                                                                        )}
                                                                    </td>
                                                                    <td className="curr-subj-type">
                                                                        <span
                                                                            className={`chip ${s.requiresLaboratory ? 'chip-lab' : 'chip-muted'}`}
                                                                            title={s.labRoomKindLabel
                                                                                ? `${s.deliveryLabel} · ${s.labRoomKindLabel}`
                                                                                : s.deliveryLabel}
                                                                        >
                                                                            {s.deliveryShort}
                                                                        </span>
                                                                    </td>
                                                                </tr>
                                                            ))}
                                                        </tbody>
                                                    </table>
                                                </div>
                                            </div>
                                        ))}
                                    </section>
                                ))}
                            </div>
                        )}
                    </>
                )}
            </section>

            {currModal && (
                <CurriculumModal
                    record={currModal.id ? currModal : null}
                    schoolYears={allSchoolYears}
                    onClose={() => setCurrModal(null)}
                    onChanged={(saved) => { refreshCurricula(); if (saved?.id) setSelectedId(saved.id); }}
                />
            )}

            {archiveOpen && (
                <ArchivedModal
                    curricula={curricula}
                    subjects={allSubjects}
                    onClose={() => setArchiveOpen(false)}
                    onRestored={() => { refreshCurricula(); refreshSubjects(); }}
                />
            )}

            {subjModal && selected && (
                <SubjectModal
                    record={subjModal.id ? subjModal : null}
                    curriculumId={selected.id}
                    candidates={subjects}
                    onClose={() => setSubjModal(null)}
                    onChanged={() => { refreshSubjects(); refreshCurricula(); }}
                />
            )}
        </div>
    );
}
