import { useEffect, useState } from 'react';
import { generateSchedule, getSchedule } from './api';
import { notifySuccess, notifyError } from '../shell/notify';
import ScheduleTable from './ScheduleTable';
import './scheduling.css';

/* FR-SCHED-06: the Academic Head runs the CSP engine for the active semester and
   reviews the generated (still-draft) schedule before it is published. On mount we
   load any existing draft so a re-run is an informed decision. */
function GenerateSchedulePage() {
    const [rows, setRows] = useState([]);
    const [semesterName, setSemesterName] = useState('');
    const [loading, setLoading] = useState(true);
    const [generating, setGenerating] = useState(false);
    const [summary, setSummary] = useState(null); // { sectionCount, assignedCount, steps }
    const [alert, setAlert] = useState(null); // { kind, text, reasons? }

    // Load any existing draft on mount so a re-run is an informed decision.
    useEffect(() => {
        let active = true;
        (async () => {
            try {
                const data = await getSchedule();
                if (!active) return;
                setRows(data.schedule);
                setSemesterName(data.semesterName);
            } catch (err) {
                if (active) setAlert({ kind: 'error', text: err.message });
            } finally {
                if (active) setLoading(false);
            }
        })();
        return () => { active = false; };
    }, []);

    async function run() {
        setGenerating(true);
        setAlert(null);
        setSummary(null);
        try {
            const data = await generateSchedule();
            setRows(data.schedule);
            setSemesterName(data.semesterName);
            setSummary({
                sectionCount: data.sectionCount,
                assignedCount: data.assignedCount,
                steps: data.steps
            });
            setAlert({
                kind: 'success',
                text: `Generated a conflict-free schedule for all ${data.assignedCount} sections.`
            });
            notifySuccess(`Generated a conflict-free schedule for all ${data.assignedCount} sections.`);
        } catch (err) {
            setAlert({
                kind: 'error',
                text: err.message,
                reasons: err.reasons
            });
            notifyError(err.message);
        } finally {
            setGenerating(false);
        }
    }

    return (
        <div className="sched-page">
            <header className="sched-head">
                <div>
                    <h2>Generate schedule</h2>
                    <p className="sched-sub">
                        Run the CSP engine to produce a conflict-free timetable for
                        {semesterName ? <> <strong>{semesterName}</strong></> : ' the active semester'}.
                        Generation replaces the current draft; published rows are never disturbed.
                    </p>
                </div>
                <button className="btn btn-primary" type="button" onClick={run} disabled={generating}>
                    {generating && <span className="spinner" aria-hidden="true" />}
                    {generating ? 'Generating…' : rows.length > 0 ? 'Regenerate' : 'Generate schedule'}
                </button>
            </header>

            {alert && (
                <div className={alert.kind === 'success' ? 'alert alert-success' : 'alert'}>
                    <p>{alert.text}</p>
                    {alert.reasons?.length > 0 && (
                        <ul className="sched-reasons">
                            {alert.reasons.map((reason, i) => <li key={i}>{reason}</li>)}
                        </ul>
                    )}
                </div>
            )}

            {summary && (
                <div className="sched-stats">
                    <div className="sched-stat">
                        <span className="sched-stat-num">{summary.assignedCount}</span>
                        <span className="sched-stat-label">Sections placed</span>
                    </div>
                    <div className="sched-stat">
                        <span className="sched-stat-num">{summary.sectionCount}</span>
                        <span className="sched-stat-label">Sections total</span>
                    </div>
                    <div className="sched-stat">
                        <span className="sched-stat-num">{summary.steps.toLocaleString()}</span>
                        <span className="sched-stat-label">Search steps</span>
                    </div>
                </div>
            )}

            {loading ? (
                <p className="sched-empty">Loading current schedule…</p>
            ) : (
                <ScheduleTable rows={rows} />
            )}
        </div>
    );
}

export default GenerateSchedulePage;
