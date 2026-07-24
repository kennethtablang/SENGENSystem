import { useEffect, useState } from 'react';
import { generateSchedule, getSchedule, getSoftConstraints, finalizeSchedule, reopenSchedule } from './api';
import { notifySuccess, notifyError } from '../shell/notify';
import { confirmAction } from '../shell/confirm';
import { hhmm } from './calendarUtils';
import ScheduleTable from './ScheduleTable';
import './scheduling.css';

const DAY_ABBR = {
    Sunday: 'Sun', Monday: 'Mon', Tuesday: 'Tue', Wednesday: 'Wed',
    Thursday: 'Thu', Friday: 'Fri', Saturday: 'Sat'
};

/* FR-SCHED-06: the Academic Head runs the CSP engine for the active semester and
   reviews the generated (still-draft) schedule before it is published. On mount we
   load any existing draft so a re-run is an informed decision. */

/* The rules the engine works under, stated on the page rather than buried in code.
   Hard constraints are guarantees; soft ones are preferences the engine optimises
   for and reports on afterwards, so the Head can judge the result. */
const hardConstraints = [
    ['No room double-booking', 'A room is never assigned two overlapping classes.'],
    ['No faculty double-assignment', 'Nobody is scheduled in two places at the same time.'],
    ['Room capacity ≥ section size', 'Checked against the section’s seat capacity, and labs are only placed in laboratories.'],
    ['Faculty load ≤ STI policy limit', 'Allocated units never exceed a member’s semester ceiling.'],
    ['Only allocated subjects', 'The engine places the load you assigned — it never invents a teaching assignment.'],
    ['No cohort clash', 'A student block never has two classes at once.']
];

const softConstraints = [
    ['Preferred time slots', 'Placements land inside a member’s declared windows where the hard constraints allow.'],
    ['Minimised idle gaps', 'Dead time between consecutive classes is measured in real minutes — for student blocks and for faculty.'],
    ['Equitable load', 'Measured and reported. The engine cannot rebalance this itself — it follows your allocation — so an uneven spread is flagged for you to correct.']
];

function ConstraintPanel() {
    return (
        <section className="sched-rules" aria-label="Scheduling constraints">
            <div className="sched-rule-col">
                <h3><span className="sched-rule-tag hard">Hard</span> Never violated</h3>
                <dl>
                    {hardConstraints.map(([name, detail]) => (
                        <div key={name}>
                            <dt>{name}</dt>
                            <dd>{detail}</dd>
                        </div>
                    ))}
                </dl>
            </div>
            <div className="sched-rule-col">
                <h3><span className="sched-rule-tag soft">Soft</span> Optimised</h3>
                <dl>
                    {softConstraints.map(([name, detail]) => (
                        <div key={name}>
                            <dt>{name}</dt>
                            <dd>{detail}</dd>
                        </div>
                    ))}
                </dl>
                <p className="sched-rule-note">
                    A soft constraint is never traded for a hard one. If the two conflict,
                    the guarantee wins and the preference is given up.
                </p>
            </div>
        </section>
    );
}

/* How the run scored on the soft constraints — the trade-offs behind the timetable. */
function OptimizationPanel({ opt }) {
    if (!opt) return null;
    return (
        <section className="sched-opt" aria-label="Optimization result">
            <h3>Soft-constraint outcome</h3>
            <div className="sched-opt-grid">
                <div>
                    <span className="sched-opt-num">{opt.preferenceHonorRatePct}%</span>
                    <span className="sched-opt-label">Time preferences honored</span>
                    <small>{opt.preferencesHonored} of {opt.preferencesApplicable} placements with a declared window</small>
                </div>
                <div>
                    <span className="sched-opt-num">{opt.cohortIdleHours} h</span>
                    <span className="sched-opt-label">Student idle time</span>
                    <small>Total gaps between a block’s consecutive classes, across the week</small>
                </div>
                <div>
                    <span className="sched-opt-num">{opt.facultyIdleHours} h</span>
                    <span className="sched-opt-label">Faculty idle time</span>
                    <small>Same measure, for teaching staff</small>
                </div>
                <div className={opt.loadLooksUneven ? 'warn' : undefined}>
                    <span className="sched-opt-num">{opt.minLoadUnits}–{opt.maxLoadUnits}</span>
                    <span className="sched-opt-label">Load spread (units)</span>
                    <small>
                        {opt.loadLooksUneven
                            ? 'Uneven — rebalance on the Faculty load page, then regenerate.'
                            : `Balanced (σ ${opt.loadSpread}).`}
                    </small>
                </div>
            </div>
        </section>
    );
}
/* The data behind the soft constraints — the basis the engine optimises toward. Shown before and
   after generation so the Academic Head can judge (and fix) the inputs, not just trust the output. */
function SoftConstraintDataPanel({ data, loading }) {
    if (loading) return <section className="sched-basis"><p className="sched-basis-empty">Loading scheduling basis…</p></section>;
    if (!data) return null;

    const { facultyCount, withPreferences, preferences, load } = data;

    return (
        <section className="sched-basis" aria-label="Soft-constraint inputs">
            <h3>Scheduling basis — soft-constraint data</h3>
            <p className="sched-basis-intro">
                What the engine optimises toward for the {facultyCount} faculty member{facultyCount === 1 ? '' : 's'} with
                load this term. These never block a schedule — they shape <em>which</em> valid schedule you get.
            </p>

            {/* S1 — preferred teaching windows */}
            <div className="sched-basis-block">
                <h4>
                    <span className="sched-rule-tag soft">S1</span> Faculty preferred time slots
                    <span className="sched-basis-count">{withPreferences}/{facultyCount} declared</span>
                </h4>
                {preferences.length === 0 ? (
                    <p className="sched-basis-empty">No faculty have a load allocation yet.</p>
                ) : (
                    <ul className="sched-pref-list">
                        {preferences.map(f => (
                            <li key={f.facultyProfileId}>
                                <span className="sched-pref-name">{f.name}</span>
                                {f.windows.length === 0 ? (
                                    <span className="sched-pref-none">No preference — may be placed any time</span>
                                ) : (
                                    <span className="sched-pref-windows">
                                        {f.windows.map((w, i) => (
                                            <span key={i} className="chip chip-muted">
                                                {DAY_ABBR[w.day] || w.day} {hhmm(w.startMinutes)}–{hhmm(w.endMinutes)}
                                            </span>
                                        ))}
                                    </span>
                                )}
                            </li>
                        ))}
                    </ul>
                )}
            </div>

            {/* S3 — equitable load distribution */}
            <div className="sched-basis-block">
                <h4>
                    <span className="sched-rule-tag soft">S3</span> Equitable load across faculty
                    <span className={`sched-basis-count${load.looksUneven ? ' warn' : ''}`}>
                        {load.minUnits}–{load.maxUnits} units {load.looksUneven ? '· uneven' : `· σ ${load.spread}`}
                    </span>
                </h4>
                {load.rows.length === 0 ? (
                    <p className="sched-basis-empty">No load allocated yet.</p>
                ) : (
                    <ul className="sched-load-list">
                        {load.rows.map(r => {
                            const pct = r.maxLoadUnits > 0 ? Math.min(100, Math.round((r.allocatedUnits / r.maxLoadUnits) * 100)) : 0;
                            const heavy = r.allocatedUnits === load.maxUnits && load.looksUneven;
                            const light = r.allocatedUnits === load.minUnits && load.looksUneven;
                            return (
                                <li key={r.facultyProfileId}>
                                    <div className="sched-load-top">
                                        <span className="sched-load-name">{r.name}</span>
                                        <span className="sched-load-val">
                                            {r.allocatedUnits}/{r.maxLoadUnits}u · {r.sectionCount} section{r.sectionCount === 1 ? '' : 's'}
                                        </span>
                                    </div>
                                    <div className="sched-load-bar">
                                        <span
                                            className={heavy ? 'is-heavy' : light ? 'is-light' : undefined}
                                            style={{ width: `${pct}%` }}
                                        />
                                    </div>
                                </li>
                            );
                        })}
                    </ul>
                )}
                {load.looksUneven && (
                    <p className="sched-basis-warn">
                        Load looks uneven — the engine can’t rebalance this, it follows your allocation.
                        Adjust it on the Faculty load page for a fairer spread, then regenerate.
                    </p>
                )}
            </div>

            {/* S2 — idle gaps (measured, not an input) */}
            <div className="sched-basis-block">
                <h4><span className="sched-rule-tag soft">S2</span> Minimised idle gaps</h4>
                <p className="sched-basis-note">
                    Idle gaps have no input to set — they’re measured on the produced timetable. After you
                    generate, the dead time between consecutive classes (for student blocks and for faculty)
                    appears in the soft-constraint outcome above.
                </p>
            </div>
        </section>
    );
}

function GenerateSchedulePage() {
    const [rows, setRows] = useState([]);
    const [semesterId, setSemesterId] = useState(null);
    const [semesterName, setSemesterName] = useState('');
    const [finalized, setFinalized] = useState(false);
    const [loading, setLoading] = useState(true);
    const [generating, setGenerating] = useState(false);
    const [finalizing, setFinalizing] = useState(false);
    const [summary, setSummary] = useState(null); // { sectionCount, assignedCount, steps }
    const [alert, setAlert] = useState(null); // { kind, text, reasons? }
    const [softData, setSoftData] = useState(null);
    const [softLoading, setSoftLoading] = useState(true);

    // Load any existing draft on mount so a re-run is an informed decision.
    useEffect(() => {
        let active = true;
        (async () => {
            try {
                const data = await getSchedule();
                if (!active) return;
                setRows(data.schedule);
                setSemesterId(data.semesterId);
                setSemesterName(data.semesterName);
                setFinalized(data.isFinalized);
            } catch (err) {
                if (active) setAlert({ kind: 'error', text: err.message });
            } finally {
                if (active) setLoading(false);
            }
        })();
        return () => { active = false; };
    }, []);

    // The soft-constraint inputs (preferred windows + load allocation) are the basis for
    // generation — load them independently so they show even before the first run.
    useEffect(() => {
        let active = true;
        (async () => {
            try {
                const data = await getSoftConstraints();
                if (active) setSoftData(data);
            } catch {
                // Non-fatal: the basis panel just won't render.
            } finally {
                if (active) setSoftLoading(false);
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
            setSemesterId(data.semesterId);
            setSemesterName(data.semesterName);
            setFinalized(false); // a freshly generated draft is not yet finalized
            setSummary({
                sectionCount: data.sectionCount,
                meetingCount: data.meetingCount,
                assignedCount: data.assignedCount,
                steps: data.steps,
                seed: data.seed,
                optimization: data.optimization
            });
            setAlert({
                kind: 'success',
                text: `Generated a conflict-free schedule: ${data.assignedCount} class meetings across ${data.sectionCount} sections.`
            });
            notifySuccess(`Generated a conflict-free schedule for all ${data.assignedCount} class meetings.`);
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

    async function finalize() {
        if (!semesterId) return;
        const ok = await confirmAction({
            title: 'Finalize this schedule?',
            message: 'The draft will be locked and marked ready for the Registrar to publish. '
                + 'You will not be able to regenerate or edit it on the board until you reopen it.',
            confirmLabel: 'Finalize'
        });
        if (!ok) return;
        setFinalizing(true);
        setAlert(null);
        try {
            const data = await finalizeSchedule(semesterId);
            setFinalized(data.isFinalized);
            setAlert({ kind: 'success', text: 'Schedule finalized — locked and ready to publish.' });
            notifySuccess('Schedule finalized — ready to publish.');
        } catch (err) {
            setAlert({ kind: 'error', text: err.message });
            notifyError(err.message);
        } finally {
            setFinalizing(false);
        }
    }

    async function reopen() {
        if (!semesterId) return;
        const ok = await confirmAction({
            title: 'Reopen this schedule?',
            message: 'The schedule will be unlocked for regeneration and board edits. '
                + 'It will no longer be marked ready to publish until you finalize it again.',
            confirmLabel: 'Reopen'
        });
        if (!ok) return;
        setFinalizing(true);
        setAlert(null);
        try {
            const data = await reopenSchedule(semesterId);
            setFinalized(data.isFinalized);
            setAlert({ kind: 'success', text: 'Schedule reopened for editing.' });
            notifySuccess('Schedule reopened for editing.');
        } catch (err) {
            setAlert({ kind: 'error', text: err.message });
            notifyError(err.message);
        } finally {
            setFinalizing(false);
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
                        The engine places the load you allocated — assign subjects to faculty on the
                        Faculty load page first, or generation will tell you what is missing.
                    </p>
                </div>
                <div className="sched-head-actions">
                    {finalized && <span className="chip chip-blue sched-final-chip">Finalized · ready to publish</span>}
                    <button
                        className={rows.length > 0 ? 'btn btn-ghost' : 'btn btn-primary'}
                        type="button"
                        onClick={run}
                        disabled={generating || finalizing || finalized}
                        title={finalized ? 'Reopen the schedule before regenerating.' : undefined}
                    >
                        {generating && <span className="spinner" aria-hidden="true" />}
                        {generating ? 'Generating…' : rows.length > 0 ? 'Regenerate' : 'Generate schedule'}
                    </button>
                    {rows.length > 0 && (
                        finalized ? (
                            <button className="btn btn-ghost" type="button" onClick={reopen} disabled={finalizing || generating}>
                                {finalizing && <span className="spinner" aria-hidden="true" />}
                                {finalizing ? 'Reopening…' : 'Reopen'}
                            </button>
                        ) : (
                            <button className="btn btn-primary" type="button" onClick={finalize} disabled={finalizing || generating}>
                                {finalizing && <span className="spinner" aria-hidden="true" />}
                                {finalizing ? 'Finalizing…' : 'Finalize'}
                            </button>
                        )
                    )}
                </div>
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
                        <span className="sched-stat-label">Meetings placed</span>
                    </div>
                    <div className="sched-stat">
                        <span className="sched-stat-num">{summary.meetingCount ?? summary.sectionCount}</span>
                        <span className="sched-stat-label">Meetings total</span>
                    </div>
                    <div className="sched-stat">
                        <span className="sched-stat-num">{summary.steps.toLocaleString()}</span>
                        <span className="sched-stat-label">Search steps</span>
                    </div>
                    <div className="sched-stat" title="The CSP engine explores the solution space differently each run. Regenerate for another valid layout; this number reproduces this exact one.">
                        <span className="sched-stat-num">#{summary.seed}</span>
                        <span className="sched-stat-label">Arrangement</span>
                    </div>
                </div>
            )}

            {summary && !finalized && (
                <p className="sched-variety-note">
                    This is one of many valid conflict-free layouts. <strong>Regenerate</strong> to
                    explore a different arrangement, then <strong>Finalize</strong> the one you prefer.
                </p>
            )}

            {summary && <OptimizationPanel opt={summary.optimization} />}

            <SoftConstraintDataPanel data={softData} loading={softLoading} />

            <ConstraintPanel />

            {loading ? (
                <p className="sched-empty">Loading current schedule…</p>
            ) : (
                <ScheduleTable rows={rows} />
            )}
        </div>
    );
}

export default GenerateSchedulePage;
