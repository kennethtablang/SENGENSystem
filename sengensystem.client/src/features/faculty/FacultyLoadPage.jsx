import { useEffect, useState } from 'react';
import { listFacultyLoad } from './api';
import FacultyAssignModal from './FacultyAssignModal';
import FacultyPreferencesModal from './FacultyPreferencesModal';
import './faculty.css';

export default function FacultyLoadPage() {
    const [semesters, setSemesters] = useState([]);
    const [semesterId, setSemesterId] = useState('');
    const [faculty, setFaculty] = useState([]);
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState(null);
    const [modal, setModal] = useState(null); // a faculty row
    const [prefsFor, setPrefsFor] = useState(null); // a faculty row (preferences editor)
    const [reload] = useState(0);

    useEffect(() => {
        let active = true;
        (async () => {
            setLoading(true); setError(null);
            try {
                const data = await listFacultyLoad(semesterId || undefined);
                if (!active) return;
                setSemesters(data.semesters);
                setFaculty(data.faculty);
                // Adopt the resolved (active) semester on first load.
                if (!semesterId && data.semesterId) setSemesterId(data.semesterId);
            } catch (err) { if (active) setError(err.message); }
            finally { if (active) setLoading(false); }
        })();
        return () => { active = false; };
    }, [semesterId, reload]);

    function onSaved(updated) {
        setFaculty(prev => prev.map(f => f.facultyProfileId === updated.facultyProfileId ? updated : f));
    }

    return (
        <div className="fl-page">
            <header className="fl-head">
                <div>
                    <h2>Faculty load</h2>
                    <p className="fl-sub">
                        Allocate subjects to faculty members for the selected semester. Each member's total
                        assigned units is checked against their teaching-load ceiling.
                    </p>
                </div>
                <label className="fl-semester">
                    <span>Semester</span>
                    <select value={semesterId} onChange={e => setSemesterId(e.target.value)} disabled={semesters.length === 0}>
                        {semesters.length === 0 && <option value="">No semesters</option>}
                        {semesters.map(s => (
                            <option key={s.id} value={s.id}>{s.name}{s.isActive ? ' (active)' : ''}</option>
                        ))}
                    </select>
                </label>
            </header>

            {error && <div className="alert">{error}</div>}

            {loading ? (
                <p className="fl-empty">Loading…</p>
            ) : faculty.length === 0 ? (
                <p className="fl-empty">No faculty members found. Add Faculty Member accounts first.</p>
            ) : (
                <div className="card fl-table-wrap">
                    <table className="fl-table">
                        <thead>
                            <tr>
                                <th>Faculty member</th>
                                <th>Department</th>
                                <th>Classes</th>
                                <th className="fl-load-col">Load</th>
                                <th></th>
                            </tr>
                        </thead>
                        <tbody>
                            {faculty.map(f => {
                                const pct = f.maxLoadUnits > 0 ? Math.min(100, Math.round((f.assignedUnits / f.maxLoadUnits) * 100)) : 0;
                                const over = f.assignedUnits > f.maxLoadUnits;
                                return (
                                    <tr key={f.facultyProfileId} className="fl-row" onClick={() => setModal(f)}>
                                        <td><strong>{f.name}</strong></td>
                                        <td className="fl-muted">{f.programCode}</td>
                                        <td className="fl-num">{f.assignedCount}</td>
                                        <td className="fl-load-col">
                                            <div className="fl-load">
                                                <div className="fl-load-bar">
                                                    <span className={`fl-load-fill${over ? ' is-over' : ''}`} style={{ width: `${pct}%` }} />
                                                </div>
                                                <span className="fl-load-text">{f.assignedUnits}/{f.maxLoadUnits}u</span>
                                            </div>
                                        </td>
                                        <td>
                                            <button
                                                className="btn"
                                                type="button"
                                                title="Preferred teaching windows (soft input to the CSP engine)"
                                                onClick={e => { e.stopPropagation(); setPrefsFor(f); }}
                                            >
                                                Preferences
                                            </button>
                                        </td>
                                    </tr>
                                );
                            })}
                        </tbody>
                    </table>
                </div>
            )}

            {modal && semesterId && (
                <FacultyAssignModal
                    faculty={modal}
                    semesterId={semesterId}
                    onClose={() => setModal(null)}
                    onSaved={onSaved}
                />
            )}

            {prefsFor && (
                <FacultyPreferencesModal
                    faculty={prefsFor}
                    onClose={() => setPrefsFor(null)}
                />
            )}
        </div>
    );
}
