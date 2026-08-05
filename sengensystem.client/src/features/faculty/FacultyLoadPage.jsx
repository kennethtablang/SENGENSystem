import { useEffect, useState } from 'react';
import { listFacultyLoad } from './api';
import FacultyAssignModal from './FacultyAssignModal';
import FacultyPreferencesModal from './FacultyPreferencesModal';
import { useTableControls } from '../shell/useTableControls';
import { SortHeader, Pagination, TableSearch } from '../shell/tableControls';
import './faculty.css';

export default function FacultyLoadPage() {
    const [semesters, setSemesters] = useState([]);
    const [semesterId, setSemesterId] = useState('');
    const [faculty, setFaculty] = useState([]);
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState(null);
    const [modal, setModal] = useState(null); // a faculty row
    const [prefsFor, setPrefsFor] = useState(null); // a faculty row (preferences editor)
    const [search, setSearch] = useState('');
    const [reload] = useState(0);

    // Sorting by load is the point of this table — it is how the Academic Head finds who is
    // over their ceiling and who has room left before allocating another subject.
    const table = useTableControls(faculty, {
        columns: {
            name: f => f.name,
            programCode: f => f.programCode,
            assignedCount: f => f.assignedCount,
            // Sort by how full they are, not raw units: 18 of 18 is a fuller load than 20 of 30.
            load: f => (f.maxLoadUnits > 0 ? f.assignedUnits / f.maxLoadUnits : 0)
        },
        initialSort: { key: 'name', dir: 'asc' },
        search,
        searchFields: [f => f.name, f => f.programCode]
    });

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
                <TableSearch value={search} onChange={setSearch} placeholder="Filter faculty or department…" />
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
            ) : table.total === 0 ? (
                <p className="fl-empty">
                    {search
                        ? 'No faculty members match your filter.'
                        : 'No faculty members found. Add Faculty Member accounts first.'}
                </p>
            ) : (
                <div className="card fl-table-wrap">
                    <table className="fl-table">
                        <thead>
                            <tr>
                                <SortHeader label="Faculty member" sortKey="name" sort={table.sort} onSort={table.toggleSort} />
                                <SortHeader label="Department" sortKey="programCode" sort={table.sort} onSort={table.toggleSort} />
                                <SortHeader label="Classes" sortKey="assignedCount" sort={table.sort} onSort={table.toggleSort} />
                                <SortHeader label="Load" sortKey="load" sort={table.sort} onSort={table.toggleSort} className="fl-load-col" />
                                <th></th>
                            </tr>
                        </thead>
                        <tbody>
                            {table.pageRows.map(f => {
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
                    <Pagination {...table} />
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
