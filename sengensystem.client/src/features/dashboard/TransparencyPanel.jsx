import { useEffect, useState } from 'react';
import { getSchedulingTransparency } from './api';

/* Scheduling transparency (FR-DASH-03): how every class in the selected semester came to be
   placed — by the CSP engine or by a manual override on the board — next to the hard rules every
   placement satisfies and the soft goals the engine optimised. Shown to the roles that answer
   for the schedule (Academic Head, Registrar, School Admin). */

const PREVIEW_ROWS = 8;

const FILTERS = [
    { key: 'all', label: 'All' },
    { key: 'engine', label: 'CSP engine' },
    { key: 'manual', label: 'Manual override' }
];

function statusOf(a) {
    if (!a.isPublished) return { label: 'Draft', chip: 'chip chip-muted' };
    return a.isAmended
        ? { label: 'Published · amended', chip: 'chip chip-yellow' }
        : { label: 'Published', chip: 'chip chip-green' };
}

export default function TransparencyPanel({ semesterId, refreshTick }) {
    const [data, setData] = useState(null);
    const [error, setError] = useState(null);
    const [filter, setFilter] = useState('all');
    const [showAll, setShowAll] = useState(false);

    useEffect(() => {
        let active = true;
        (async () => {
            try {
                const payload = await getSchedulingTransparency(semesterId);
                if (active) { setData(payload); setError(null); }
            } catch (err) {
                if (active) setError(err.message);
            }
        })();
        return () => { active = false; };
    }, [semesterId, refreshTick]);

    if (error) {
        return (
            <section className="card dash-panel dash-transparency">
                <h3>Scheduling transparency</h3>
                <p className="dash-panel-empty">{error}</p>
            </section>
        );
    }
    if (!data) {
        return (
            <section className="card dash-panel dash-transparency">
                <h3>Scheduling transparency</h3>
                <p className="dash-panel-empty">Loading…</p>
            </section>
        );
    }

    const { summary, engine, assignments } = data;
    const filtered = assignments.filter(a =>
        filter === 'all'
        || (filter === 'engine' && a.provenance === 'CSP engine')
        || (filter === 'manual' && a.provenance === 'Manual override'));
    const rows = showAll ? filtered : filtered.slice(0, PREVIEW_ROWS);

    return (
        <section className="card dash-panel dash-transparency" aria-labelledby="dash-transparency-title">
            <h3 id="dash-transparency-title">
                Scheduling transparency <small>(how each class was placed)</small>
            </h3>

            <ul className="dash-transparency-stats">
                <li><strong>{summary.total}</strong><span>Classes placed</span></li>
                <li><strong>{summary.engineGenerated}</strong><span>By the CSP engine</span></li>
                <li><strong>{summary.manualOverrides}</strong><span>Manual overrides</span></li>
                <li><strong>{summary.published}</strong><span>Published</span></li>
                <li><strong>{summary.amended ?? 0}</strong><span>Amended after publishing</span></li>
            </ul>

            <p className="dash-transparency-engine">{engine.kind}</p>

            <div className="dash-transparency-rules">
                <div>
                    <h4>Hard rules <small>(never broken)</small></h4>
                    <ul>{engine.hardConstraints.map(c => <li key={c}>{c}</li>)}</ul>
                </div>
                <div>
                    <h4>Soft goals <small>(optimized)</small></h4>
                    <ul>{engine.softFactors.map(f => <li key={f}>{f}</li>)}</ul>
                </div>
            </div>

            {assignments.length === 0 ? (
                <p className="dash-panel-empty">Nothing scheduled this semester yet.</p>
            ) : (
                <>
                    <div className="dash-transparency-filter" role="group" aria-label="Filter by how the class was placed">
                        {FILTERS.map(f => (
                            <button
                                key={f.key}
                                type="button"
                                className={`btn btn-sm ${filter === f.key ? 'btn-primary' : 'btn-ghost'}`}
                                aria-pressed={filter === f.key}
                                onClick={() => { setFilter(f.key); setShowAll(false); }}
                            >
                                {f.label}
                            </button>
                        ))}
                    </div>

                    {filtered.length === 0 ? (
                        <p className="dash-panel-empty">No classes match this filter.</p>
                    ) : (
                        <div className="dash-transparency-table-wrap">
                            <table className="dash-transparency-table">
                                <thead>
                                    <tr>
                                        <th scope="col">Subject</th>
                                        <th scope="col">Cohort</th>
                                        <th scope="col">Day &amp; time</th>
                                        <th scope="col">Room</th>
                                        <th scope="col">Faculty</th>
                                        <th scope="col">Placed by</th>
                                        <th scope="col">Status</th>
                                    </tr>
                                </thead>
                                <tbody>
                                    {rows.map((a, i) => {
                                        const status = statusOf(a);
                                        return (
                                            <tr key={`${a.sectionCode}-${a.day}-${a.time}-${i}`}>
                                                <td><strong>{a.subjectCode}</strong> {a.sectionCode}</td>
                                                <td>{a.cohort || '—'}</td>
                                                <td>{a.day} {a.time}</td>
                                                <td>{a.room || '—'}</td>
                                                <td>{a.faculty || '—'}</td>
                                                <td>
                                                    <span className={a.provenance === 'Manual override' ? 'chip chip-yellow' : 'chip chip-blue'}>
                                                        {a.provenance}
                                                    </span>
                                                </td>
                                                <td><span className={status.chip}>{status.label}</span></td>
                                            </tr>
                                        );
                                    })}
                                </tbody>
                            </table>
                        </div>
                    )}

                    {filtered.length > PREVIEW_ROWS && (
                        <button type="button" className="btn btn-sm btn-ghost dash-transparency-more"
                            onClick={() => setShowAll(v => !v)}>
                            {showAll ? 'Show fewer' : `Show all ${filtered.length} classes`}
                        </button>
                    )}
                </>
            )}
        </section>
    );
}
