import { useCallback, useEffect, useState } from 'react';
import { generateSchedule, getSchedule, getSoftConstraints, updateSoftConstraintWeights, finalizeSchedule, reopenSchedule } from './api';
import { notifySuccess, notifyError } from '../shell/notify';
import { confirmAction } from '../shell/confirm';
import { confirmsHeavy } from '../settings/prefs';
import { hhmm } from './calendarUtils';
import ScheduleTable from './ScheduleTable';
import ThinkingOverlay from './ThinkingOverlay';
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
/* The times of day a class may be told to start, on the half hour across a plausible teaching
   day. Half-hour steps match the period grid, so a chosen start always lands on a slot boundary. */
const CLASS_START_CHOICES = Array.from({ length: 25 }, (_, i) => 6 * 60 + i * 30); // 06:00–18:00

/* FR-SCHED-05: the tunable generation settings. The Academic Head sets when the teaching day
   opens and leans the engine toward the concerns the institution cares about most; the values
   apply on the next generation. Hard constraints stay guarantees regardless of these. */
function ConstraintWeightsPanel({ weights, slots, disabled, onSaved }) {
    const [draft, setDraft] = useState(null);
    const [saving, setSaving] = useState(false);

    const snapshot = (w) => ({
        preference: w.preference,
        idleGap: w.idleGap,
        roomFit: w.roomFit,
        gapSaturationHours: w.gapSaturationHours,
        classStartMinutes: w.classStartMinutes ?? 7 * 60
    });

    useEffect(() => {
        if (weights) setDraft(snapshot(weights));
    }, [weights]);

    if (!draft) return null;

    const dirty = !!weights && (
        draft.preference !== weights.preference
        || draft.idleGap !== weights.idleGap
        || draft.roomFit !== weights.roomFit
        || draft.gapSaturationHours !== weights.gapSaturationHours
        || draft.classStartMinutes !== (weights.classStartMinutes ?? 7 * 60)
    );

    const setField = (key, value) => setDraft(prev => ({ ...prev, [key]: value }));
    const reset = () => setDraft(snapshot(weights));

    // Saved setting vs. the grid it has to live on — a start time past every period would leave
    // the engine nothing to place, so say so here instead of failing on the Generate button.
    const savedStart = weights.classStartMinutes ?? 7 * 60;
    const noUsableSlots = slots && slots.allowableSlotCount > 0 && slots.usableSlotCount === 0;
    const trimmedSlots = slots ? slots.allowableSlotCount - slots.usableSlotCount : 0;

    async function save() {
        setSaving(true);
        try {
            const updated = await updateSoftConstraintWeights(draft);
            onSaved(updated);
            notifySuccess('Generation settings saved — they apply on the next generation.');
        } catch (err) {
            notifyError(err.message);
        } finally {
            setSaving(false);
        }
    }

    const sliders = [
        ['preference', 'Preferred time slots (S1)', 'Reward for landing inside a faculty member’s declared window.'],
        ['idleGap', 'Minimised idle gaps (S2)', 'Penalty for dead time between a cohort’s or member’s consecutive classes.'],
        ['roomFit', 'Room fit', 'Reward for a snugly sized room over an oversized one.']
    ];

    return (
        <section className="sched-weights" aria-label="Generation settings">
            <h3>Generation settings</h3>
            <p className="sched-basis-intro">
                When the teaching day opens, and how hard the engine leans on each preference. Weights are
                relative (0–1) and normalised before weighting — raise one to give that concern more pull.
                Hard constraints are never traded for these. Changes apply on the next generation.
            </p>

            <div className="sched-daystart">
                <label className="sched-weight">
                    <span className="sched-weight-top">
                        <span>Classes start at</span>
                        <span className="sched-weight-val">{hhmm(draft.classStartMinutes)}</span>
                    </span>
                    <select
                        value={draft.classStartMinutes}
                        disabled={disabled || saving}
                        onChange={e => setField('classStartMinutes', parseInt(e.target.value, 10))}
                    >
                        {CLASS_START_CHOICES.map(m => (
                            <option key={m} value={m}>{hhmm(m)}</option>
                        ))}
                    </select>
                    <small>
                        The earliest a generated class may be placed. Periods that begin before this are
                        left out of the engine&rsquo;s grid — nothing is scheduled ahead of the school day.
                        Manual placements on the board are not restricted by it.
                    </small>
                </label>
                {noUsableSlots ? (
                    <p className="sched-basis-warn">
                        No allowable period starts at or after {hhmm(savedStart)} — generation has nothing to
                        place. Move the start time earlier, or add later periods in System parameters.
                    </p>
                ) : trimmedSlots > 0 && (
                    <p className="sched-basis-note">
                        {trimmedSlots} of {slots.allowableSlotCount} periods start before {hhmm(savedStart)} and
                        are left out of generation.
                    </p>
                )}
            </div>

            <div className="sched-weights-grid">
                {sliders.map(([key, label, hint]) => (
                    <label key={key} className="sched-weight">
                        <span className="sched-weight-top">
                            <span>{label}</span>
                            <span className="sched-weight-val">{Number(draft[key]).toFixed(2)}</span>
                        </span>
                        <input
                            type="range" min="0" max="1" step="0.05"
                            value={draft[key]}
                            disabled={disabled || saving}
                            onChange={e => setField(key, parseFloat(e.target.value))}
                        />
                        <small>{hint}</small>
                    </label>
                ))}
                <label className="sched-weight">
                    <span className="sched-weight-top">
                        <span>Idle-gap saturation</span>
                        <span className="sched-weight-val">{Number(draft.gapSaturationHours).toFixed(1)} h</span>
                    </span>
                    <input
                        type="range" min="1" max="24" step="0.5"
                        value={draft.gapSaturationHours}
                        disabled={disabled || saving}
                        onChange={e => setField('gapSaturationHours', parseFloat(e.target.value))}
                    />
                    <small>The longest idle gap treated as “as bad as it gets”.</small>
                </label>
            </div>
            <div className="sched-weights-actions">
                <button className="btn btn-ghost" type="button" onClick={reset} disabled={!dirty || saving || disabled}>
                    Reset
                </button>
                <button className="btn btn-primary" type="button" onClick={save} disabled={!dirty || saving || disabled}>
                    {saving && <span className="spinner" aria-hidden="true" />}
                    {saving ? 'Saving…' : 'Save settings'}
                </button>
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
    // generation — load them independently so they show even before the first run. Re-read after
    // a settings save too: the class start time changes how much of the period grid is usable.
    const reloadSoftData = useCallback(async () => {
        try {
            setSoftData(await getSoftConstraints());
        } catch {
            // Non-fatal: the basis panel keeps whatever it last had.
        }
    }, []);

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
        // Second thought before a heavy run (the CSP search can take up to ~20s), and because
        // generating REPLACES the term's timetable rather than adding to it — honors the
        // "Confirm heavy actions" pref.
        const isRegenerate = rows.length > 0;
        if (confirmsHeavy()) {
            const ok = await confirmAction({
                title: isRegenerate ? 'Regenerate the schedule?' : 'Generate the schedule?',
                message: (isRegenerate
                    ? 'This throws away the current schedule for this term and builds a completely new '
                      + 'conflict-free arrangement from scratch — it does not add to what is on the board. '
                    : 'This runs the scheduling engine across every section, room, and time slot to build a conflict-free timetable. ')
                    + 'It can take up to about 20 seconds — keep this tab open while it runs.',
                confirmLabel: isRegenerate ? 'Regenerate' : 'Generate'
            });
            if (!ok) return;
        }
        await execute(false);
    }

    /* The run itself, separated so the published-rewrite path can retry it with consent.
       `replacePublished` is never defaulted on: the server refuses without it, and the only way it
       becomes true is the second confirmation below. */
    async function execute(replacePublished) {
        setGenerating(true);
        setAlert(null);
        setSummary(null);
        try {
            const data = await generateSchedule(null, { replacePublished });
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
            const replacedNote = data.replacedPublishedCount > 0
                ? ` The previously published timetable (${data.replacedPublishedCount} placements) was replaced —`
                  + ' publish this term again so students and faculty see the new one.'
                : '';
            setAlert({
                kind: 'success',
                text: `Generated a conflict-free schedule: ${data.assignedCount} class meetings across `
                    + `${data.sectionCount} sections.${replacedNote}`
            });
            notifySuccess(`Generated a conflict-free schedule for all ${data.assignedCount} class meetings.`);
        } catch (err) {
            // The server found a published timetable and is asking whether we really mean it. This
            // confirmation is NOT behind the "Confirm heavy actions" preference: turning off a
            // convenience prompt is not consent to discard something already sent to students.
            if (err.requiresConfirmation) {
                setGenerating(false);
                const ok = await confirmAction({
                    title: 'Rewrite the published schedule?',
                    message: `${semesterName || 'This term'} already has a published schedule. Regenerating `
                        + `deletes all ${err.publishedCount} published class placements across `
                        + `${err.publishedSections} sections and builds a brand-new timetable in their place.`
                        + (err.affectedStudents > 0
                            ? ` ${err.affectedStudents} students hold approved seats — their class days, times, `
                              + 'and rooms will change.'
                            : '')
                        + ' The result comes back as an unpublished draft, so you must publish the term again '
                        + 'before anyone sees it. This cannot be undone.',
                    confirmLabel: 'Yes, rewrite it',
                    danger: true
                });
                if (ok) await execute(true);
                return;
            }
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
            {generating && <ThinkingOverlay />}
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

            <ConstraintWeightsPanel
                weights={softData?.weights}
                slots={softData && {
                    allowableSlotCount: softData.allowableSlotCount ?? 0,
                    usableSlotCount: softData.usableSlotCount ?? 0
                }}
                disabled={generating || finalized}
                onSaved={reloadSoftData}
            />

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
