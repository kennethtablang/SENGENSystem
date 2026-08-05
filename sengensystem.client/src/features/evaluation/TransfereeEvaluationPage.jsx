import { useCallback, useEffect, useMemo, useState } from 'react';
import { createPortal } from 'react-dom';
import {
    listEvaluations, getEvaluation, saveEvaluation, completeEvaluation, reopenEvaluation,
    downloadEvaluationSheet
} from './api';
import { notifySuccess, notifyError } from '../shell/notify';
import { confirmAction } from '../shell/confirm';
import { humanize, formatPHT } from '../registration/options';
import { useServerTable } from '../shell/useServerTable';
import { SortHeader, Pagination } from '../shell/tableControls';
import '../registration/registration.css';
import './evaluation.css';

/* FR-EVAL: the Registrar rules, subject by subject, on what a transferee's previous schooling
   counts for here. That ruling settles two things at once — the subjects the student still has to
   take, and the year level their credited units earn them — and until it is signed off the student
   cannot enlist, because there is no honest answer yet to "which subjects are yours".

   The page is a queue and a sheet: pick a transferee, work down their curriculum, complete. */

const STATUS_FILTERS = ['All', 'Pending', 'InProgress', 'Completed'];

const statusChip = {
    Pending: 'chip chip-yellow',
    InProgress: 'chip chip-blue',
    Completed: 'chip chip-blue'
};

const statusLabel = {
    Pending: 'Not started',
    InProgress: 'In progress',
    Completed: 'Completed'
};

/* Sorting moved to the server (TransfereeEvaluationEndpoints) along with the paging — see
   useServerTable. `status` and `creditedUnits` are computed from the evaluation's items rather
   than stored on the registration, so they sort through a subquery there. */

const YEAR_CHOICES = [1, 2, 3, 4];
const yearLabel = (n) => (['1st year', '2nd year', '3rd year', '4th year'][n - 1] ?? `Year ${n}`);

export default function TransfereeEvaluationPage() {
    const [rows, setRows] = useState([]);
    const [total, setTotal] = useState(0);
    const [counts, setCounts] = useState({ pendingCount: 0, completedCount: 0 });
    const [loading, setLoading] = useState(true);
    const [filter, setFilter] = useState('All');
    const [search, setSearch] = useState('');
    const [appliedSearch, setAppliedSearch] = useState('');
    const [reload, setReload] = useState(0);
    const [alert, setAlert] = useState(null);
    const [openId, setOpenId] = useState(null);

    const table = useServerTable({
        rows,
        total,
        initialSort: { key: 'fullName', dir: 'asc' },
        search: appliedSearch
    });

    useEffect(() => {
        let active = true;
        (async () => {
            setLoading(true);
            try {
                const data = await listEvaluations({ status: filter, ...table.query });
                if (!active) return;
                setRows(data.evaluations);
                setTotal(data.total);
                // Counted server-side over the whole queue and independent of the status chip, so
                // the two figures stay a statement about the term's outstanding work.
                setCounts({ pendingCount: data.pendingCount, completedCount: data.completedCount });
            } catch (err) {
                if (active) setAlert({ kind: 'error', text: err.message });
            } finally {
                if (active) setLoading(false);
            }
        })();
        return () => { active = false; };
    // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [filter, table.queryKey, reload]);

    const refreshQueue = useCallback(() => setReload(v => v + 1), []);

    return (
        <div className="reg-page">
            <header className="reg-head">
                <div>
                    <h2>Evaluate transferee</h2>
                    <p className="reg-sub">
                        Rule on each subject a transferee brings from their previous school. Credited subjects
                        are not retaken; the rest is their load here. The units you credit decide the year level
                        they enter at — and a transferee cannot enlist until this is completed.
                    </p>
                </div>
                <div className="reg-controls">
                    <form onSubmit={e => { e.preventDefault(); setAppliedSearch(search); }} className="reg-search">
                        <input
                            type="search" placeholder="Search name or student no."
                            value={search} onChange={e => setSearch(e.target.value)}
                        />
                    </form>
                    <label className="reg-filter">
                        <span>Show</span>
                        <select value={filter} onChange={e => setFilter(e.target.value)}>
                            {STATUS_FILTERS.map(f => (
                                <option key={f} value={f}>{f === 'All' ? 'All' : statusLabel[f]}</option>
                            ))}
                        </select>
                    </label>
                </div>
            </header>

            {alert && (
                <div className={alert.kind === 'success' ? 'alert alert-success' : 'alert'}>
                    <p>{alert.text}</p>
                    {alert.reasons?.length > 0 && (
                        <ul className="eval-reasons">
                            {alert.reasons.map((r, i) => <li key={i}>{r}</li>)}
                        </ul>
                    )}
                </div>
            )}

            {loading ? (
                <p className="reg-empty">Loading…</p>
            ) : rows.length === 0 ? (
                <p className="reg-empty">
                    {appliedSearch
                        ? 'No transferees match your search.'
                        : 'No transferee registrations for this view.'}
                </p>
            ) : (
                <div className="card reg-table-wrap">
                    <table className="reg-table">
                        <thead>
                            <tr>
                                <SortHeader label="Student no." sortKey="studentNumber" sort={table.sort} onSort={table.toggleSort} />
                                <SortHeader label="Name" sortKey="fullName" sort={table.sort} onSort={table.toggleSort} />
                                <SortHeader label="Program" sortKey="program" sort={table.sort} onSort={table.toggleSort} />
                                <SortHeader label="Credited" sortKey="creditedUnits" sort={table.sort} onSort={table.toggleSort} />
                                <SortHeader label="Year level" sortKey="yearLevel" sort={table.sort} onSort={table.toggleSort} />
                                <SortHeader label="Evaluation" sortKey="status" sort={table.sort} onSort={table.toggleSort} />
                                <th></th>
                            </tr>
                        </thead>
                        <tbody>
                            {table.pageRows.map(row => (
                                <tr key={row.registrationId}>
                                    <td className="reg-mono">
                                        {row.officialStudentNumber || row.registrationNumber}
                                        {!row.officialStudentNumber && (
                                            <span className="reg-when">Registration no. — student no. not yet issued</span>
                                        )}
                                    </td>
                                    <td><strong>{row.fullName}</strong></td>
                                    <td>{row.program}</td>
                                    <td>
                                        {row.status === 'Pending'
                                            ? <span className="reg-when">—</span>
                                            : <>{row.creditedUnits}u <span className="reg-when">{row.toTakeUnits}u to take</span></>}
                                    </td>
                                    <td>{yearLabel(row.yearLevel)}</td>
                                    <td>
                                        <span className={statusChip[row.status] || 'chip chip-muted'}>
                                            {statusLabel[row.status] || humanize(row.status)}
                                        </span>
                                        {row.evaluatedAtUtc && (
                                            <span className="reg-when">{formatPHT(row.evaluatedAtUtc)}</span>
                                        )}
                                    </td>
                                    <td className="reg-actions">
                                        <button
                                            className={row.status === 'Completed' ? 'btn btn-sm' : 'btn btn-primary btn-sm'}
                                            type="button"
                                            onClick={() => setOpenId(row.registrationId)}
                                        >
                                            {row.status === 'Completed' ? 'View' : 'Evaluate'}
                                        </button>
                                    </td>
                                </tr>
                            ))}
                        </tbody>
                    </table>
                    <Pagination {...table} />
                </div>
            )}

            <p className="doc-footnote">
                {counts.pendingCount} transferee{counts.pendingCount === 1 ? '' : 's'} awaiting evaluation ·
                {' '}{counts.completedCount} completed.
            </p>

            {openId && (
                <EvaluationSheet
                    registrationId={openId}
                    onClose={() => setOpenId(null)}
                    onChanged={refreshQueue}
                    onAlert={setAlert}
                />
            )}
        </div>
    );
}

/* The sheet itself: the student's whole curriculum, in curriculum order, one verdict per subject. */
function EvaluationSheet({ registrationId, onClose, onChanged, onAlert }) {
    const [sheet, setSheet] = useState(null);
    const [error, setError] = useState('');
    const [busy, setBusy] = useState(false);
    // Local, unsaved verdicts keyed by subject id, so a long curriculum can be worked through
    // without a round trip per click.
    const [draft, setDraft] = useState({});
    const [remarks, setRemarks] = useState('');
    const [yearOverride, setYearOverride] = useState(null);

    useEffect(() => {
        let active = true;
        (async () => {
            try {
                const data = await getEvaluation(registrationId);
                if (!active) return;
                setSheet(data);
                setRemarks(data.remarks ?? '');
                setDraft(Object.fromEntries(data.subjects.map(s => [s.subjectId, {
                    decision: s.decision,
                    sourceSubject: s.sourceSubject ?? '',
                    sourceGrade: s.sourceGrade ?? ''
                }])));
            } catch (err) {
                if (active) setError(err.message);
            }
        })();
        return () => { active = false; };
    }, [registrationId]);

    // Esc closes, matching every other modal on the system.
    useEffect(() => {
        const onKey = (e) => { if (e.key === 'Escape' && !busy) onClose(); };
        window.addEventListener('keydown', onKey);
        document.body.style.overflow = 'hidden';
        return () => {
            window.removeEventListener('keydown', onKey);
            document.body.style.overflow = '';
        };
    }, [busy, onClose]);

    // Live totals from the local draft, so crediting a subject moves the units and the derived
    // year level immediately — the Registrar sees the consequence of the decision as they make it.
    const totals = useMemo(() => {
        if (!sheet) return { credited: 0, toTake: 0, undecided: 0, unitsByYear: {} };
        let credited = 0, toTake = 0, undecided = 0;
        const unitsByYear = {};
        for (const s of sheet.subjects) {
            unitsByYear[s.yearLevel] = (unitsByYear[s.yearLevel] ?? 0) + s.units;
            const decision = draft[s.subjectId]?.decision ?? 'Undecided';
            if (decision === 'Credited') credited += s.units;
            else if (decision === 'ToTake') toTake += s.units;
            else undecided += 1;
        }
        return { credited, toTake, undecided, unitsByYear };
    }, [sheet, draft]);

    // Mirrors YearLevelPolicy.FromCreditedUnits on the server: a student is in year N once they
    // have been credited everything years 1..N-1 ask for.
    const derivedYear = useMemo(() => {
        let year = 1;
        let cumulative = 0;
        for (let candidate = 1; candidate < 4; candidate++) {
            const yearUnits = totals.unitsByYear[candidate] ?? 0;
            if (yearUnits <= 0) break;
            cumulative += yearUnits;
            if (totals.credited < cumulative) break;
            year = candidate + 1;
        }
        return year;
    }, [totals]);

    const grouped = useMemo(() => {
        if (!sheet) return [];
        const map = new Map();
        for (const s of sheet.subjects) {
            const key = `${s.yearLevel}|${s.term}`;
            if (!map.has(key)) map.set(key, { yearLevel: s.yearLevel, termLabel: s.termLabel, subjects: [] });
            map.get(key).subjects.push(s);
        }
        return [...map.values()].sort((a, b) =>
            a.yearLevel - b.yearLevel || a.termLabel.localeCompare(b.termLabel));
    }, [sheet]);

    const completed = sheet?.status === 'Completed';

    function setDecision(subjectId, decision) {
        setDraft(prev => ({
            ...prev,
            [subjectId]: {
                ...prev[subjectId],
                decision,
                // Provenance only means something on a credit — drop it when the verdict changes,
                // so a "to take" row can't keep a stale source subject hanging off it.
                ...(decision === 'Credited' ? {} : { sourceSubject: '', sourceGrade: '' })
            }
        }));
    }

    function setSource(subjectId, field, value) {
        setDraft(prev => ({ ...prev, [subjectId]: { ...prev[subjectId], [field]: value } }));
    }

    /** Rule a whole term block at once — most transferees credit or carry a term wholesale. */
    function setBlock(subjects, decision) {
        setDraft(prev => {
            const next = { ...prev };
            for (const s of subjects) {
                next[s.subjectId] = {
                    ...next[s.subjectId],
                    decision,
                    ...(decision === 'Credited' ? {} : { sourceSubject: '', sourceGrade: '' })
                };
            }
            return next;
        });
    }

    const items = () => Object.entries(draft)
        .filter(([, v]) => v.decision && v.decision !== 'Undecided')
        .map(([subjectId, v]) => ({
            subjectId,
            decision: v.decision,
            sourceSubject: v.sourceSubject || null,
            sourceGrade: v.sourceGrade || null
        }));

    async function save() {
        setBusy(true);
        try {
            const data = await saveEvaluation(registrationId, { items: items(), remarks });
            setSheet(data);
            notifySuccess('Evaluation saved.');
            onChanged();
        } catch (err) {
            setError(err.message);
            notifyError(err.message);
        } finally {
            setBusy(false);
        }
    }

    async function complete() {
        const assigned = yearOverride ?? derivedYear;
        const ok = await confirmAction({
            title: `Complete ${sheet.fullName}'s evaluation?`,
            message: `${totals.credited} units will be credited and ${totals.toTake} units carried as their `
                + `load here. They will enter as ${yearLabel(assigned)} and be able to enlist. `
                + 'You can reopen this later to correct it.',
            confirmLabel: 'Complete evaluation'
        });
        if (!ok) return;

        setBusy(true);
        try {
            // Save first so the sign-off rules on exactly what is on screen.
            await saveEvaluation(registrationId, { items: items(), remarks });
            const data = await completeEvaluation(registrationId, { assignedYearLevel: assigned, remarks });
            setSheet(data);
            notifySuccess(`${data.fullName} evaluated — entering as ${yearLabel(data.assignedYearLevel)}.`);
            onAlert({
                kind: 'success',
                text: `${data.fullName}: ${data.creditedUnits} units credited, ${data.toTakeUnits} to take, `
                    + `entering as ${yearLabel(data.assignedYearLevel)}. They can now enlist.`
            });
            onChanged();
        } catch (err) {
            setError(err.message);
            onAlert({ kind: 'error', text: err.message, reasons: err.reasons });
            notifyError(err.message);
        } finally {
            setBusy(false);
        }
    }

    async function reopen() {
        const ok = await confirmAction({
            title: 'Reopen this evaluation?',
            message: 'The decisions are kept, but the student will not be able to enlist until you '
                + 'complete it again.',
            confirmLabel: 'Reopen'
        });
        if (!ok) return;
        setBusy(true);
        try {
            const data = await reopenEvaluation(registrationId);
            setSheet(data);
            notifySuccess('Evaluation reopened.');
            onChanged();
        } catch (err) {
            setError(err.message);
            notifyError(err.message);
        } finally {
            setBusy(false);
        }
    }

    /* Rendered into <body>: the page root carries a filling `rise` transform animation, which makes
       it a containing block, and a fixed overlay nested inside it would be pinned to the page box
       instead of the viewport — off-centre, with only the page area dimmed. */
    return createPortal(
        <div className="modal-overlay" onClick={() => !busy && onClose()} role="presentation">
            <div
                className="modal eval-modal"
                role="dialog" aria-modal="true" aria-label="Transferee evaluation"
                onClick={e => e.stopPropagation()}
            >
                <header className="modal-head">
                    <h2>{sheet ? sheet.fullName : 'Transferee evaluation'}</h2>
                    <button type="button" className="modal-close" onClick={onClose} aria-label="Close" disabled={busy}>×</button>
                </header>

                <div className="modal-body">
                    {error && <div className="alert">{error}</div>}
                    {!sheet ? (
                        <p className="reg-empty">Loading the curriculum…</p>
                    ) : (
                        <>
                            <div className="eval-meta">
                                <span className="reg-mono">{sheet.officialStudentNumber || sheet.registrationNumber}</span>
                                <span>·</span>
                                <span>{sheet.program}</span>
                                {sheet.schoolName && <><span>·</span><span>from {sheet.schoolName}</span></>}
                                {sheet.curriculumName && <><span>·</span><span>{sheet.curriculumName}</span></>}
                            </div>

                            {sheet.subjects.length === 0 ? (
                                <p className="reg-empty">
                                    No curriculum is set up for {sheet.program} yet — add its subjects under
                                    Subjects &amp; curriculum before evaluating.
                                </p>
                            ) : (
                                <>
                                    <div className="eval-stats">
                                        <div><span className="eval-stat-num">{totals.credited}</span><span>units credited</span></div>
                                        <div><span className="eval-stat-num">{totals.toTake}</span><span>units to take</span></div>
                                        <div className={totals.undecided > 0 ? 'warn' : undefined}>
                                            <span className="eval-stat-num">{totals.undecided}</span><span>still undecided</span>
                                        </div>
                                        <div>
                                            <span className="eval-stat-num">{yearLabel(yearOverride ?? derivedYear)}</span>
                                            <span>{yearOverride ? 'set by you' : 'derived year level'}</span>
                                        </div>
                                    </div>

                                    {completed && (
                                        <div className="alert alert-success eval-done">
                                            Completed {formatPHT(sheet.evaluatedAtUtc)} — entering as{' '}
                                            {yearLabel(sheet.assignedYearLevel)}. Reopen to correct it.
                                        </div>
                                    )}

                                    {grouped.map(block => {
                                        const allCredited = block.subjects.every(s => draft[s.subjectId]?.decision === 'Credited');
                                        const allToTake = block.subjects.every(s => draft[s.subjectId]?.decision === 'ToTake');
                                        return (
                                            <section className="eval-block" key={`${block.yearLevel}-${block.termLabel}`}>
                                                <h4>
                                                    {yearLabel(block.yearLevel)} · {block.termLabel}
                                                    <span className="eval-block-units">
                                                        {block.subjects.reduce((sum, s) => sum + s.units, 0)}u
                                                    </span>
                                                    {!completed && (
                                                        <span className="eval-block-actions">
                                                            <button
                                                                type="button"
                                                                className={`btn btn-sm ${allCredited ? 'btn-primary' : 'btn-ghost'}`}
                                                                onClick={() => setBlock(block.subjects, 'Credited')}
                                                            >Credit all</button>
                                                            <button
                                                                type="button"
                                                                className={`btn btn-sm ${allToTake ? 'btn-primary' : 'btn-ghost'}`}
                                                                onClick={() => setBlock(block.subjects, 'ToTake')}
                                                            >All to take</button>
                                                        </span>
                                                    )}
                                                </h4>
                                                <ul className="eval-list">
                                                    {block.subjects.map(s => {
                                                        const d = draft[s.subjectId] ?? {};
                                                        const credited = d.decision === 'Credited';
                                                        return (
                                                            <li
                                                                key={s.subjectId}
                                                                className={`eval-item${credited ? ' is-credited' : ''}${d.decision === 'Undecided' || !d.decision ? ' is-undecided' : ''}`}
                                                            >
                                                                <div className="eval-item-main">
                                                                    <span className="eval-code">{s.code}</span>
                                                                    <span className="eval-title">{s.title}</span>
                                                                    <span className="eval-units">{s.units}u</span>
                                                                </div>
                                                                <div className="eval-item-decide">
                                                                    {['Credited', 'ToTake'].map(value => (
                                                                        <label key={value} className="eval-radio">
                                                                            <input
                                                                                type="radio"
                                                                                name={`decision-${s.subjectId}`}
                                                                                checked={d.decision === value}
                                                                                disabled={completed || busy}
                                                                                onChange={() => setDecision(s.subjectId, value)}
                                                                            />
                                                                            <span>{value === 'Credited' ? 'Credited' : 'To take'}</span>
                                                                        </label>
                                                                    ))}
                                                                </div>
                                                                {credited && (
                                                                    <div className="eval-item-source">
                                                                        <input
                                                                            type="text"
                                                                            placeholder="Credited from (e.g. IT100 — Intro to IT)"
                                                                            value={d.sourceSubject ?? ''}
                                                                            disabled={completed || busy}
                                                                            onChange={e => setSource(s.subjectId, 'sourceSubject', e.target.value)}
                                                                        />
                                                                        <input
                                                                            type="text"
                                                                            className="eval-grade"
                                                                            placeholder="Grade"
                                                                            value={d.sourceGrade ?? ''}
                                                                            disabled={completed || busy}
                                                                            onChange={e => setSource(s.subjectId, 'sourceGrade', e.target.value)}
                                                                        />
                                                                    </div>
                                                                )}
                                                            </li>
                                                        );
                                                    })}
                                                </ul>
                                            </section>
                                        );
                                    })}

                                    <label className="eval-remarks">
                                        <span>Remarks <em>(printed on the evaluation sheet)</em></span>
                                        <textarea
                                            rows={2}
                                            value={remarks}
                                            disabled={completed || busy}
                                            placeholder="e.g. Credits verified against the OTR from STI Dagupan."
                                            onChange={e => setRemarks(e.target.value)}
                                        />
                                    </label>

                                    {!completed && (
                                        <label className="eval-year">
                                            <span>Year level to assign</span>
                                            <select
                                                value={yearOverride ?? derivedYear}
                                                disabled={busy}
                                                onChange={e => setYearOverride(parseInt(e.target.value, 10))}
                                            >
                                                {YEAR_CHOICES.map(y => (
                                                    <option key={y} value={y}>
                                                        {yearLabel(y)}{y === derivedYear ? ' — derived from credits' : ''}
                                                    </option>
                                                ))}
                                            </select>
                                            <small>
                                                The credited units derive {yearLabel(derivedYear)}. Override it only when
                                                you have a reason the units don’t capture.
                                            </small>
                                        </label>
                                    )}
                                </>
                            )}
                        </>
                    )}
                </div>

                <footer className="modal-foot">
                    {sheet?.status === 'Completed' && (
                        <button
                            type="button" className="btn btn-ghost" disabled={busy}
                            onClick={() => downloadEvaluationSheet(registrationId, sheet.officialStudentNumber || sheet.registrationNumber)
                                .catch(err => notifyError(err.message))}
                        >
                            Download sheet (PDF)
                        </button>
                    )}
                    <span className="setup-foot-spacer" />
                    <button type="button" className="btn btn-ghost" onClick={onClose} disabled={busy}>Close</button>
                    {sheet && sheet.subjects.length > 0 && (completed ? (
                        <button type="button" className="btn" onClick={reopen} disabled={busy}>
                            {busy ? 'Working…' : 'Reopen'}
                        </button>
                    ) : (
                        <>
                            <button type="button" className="btn" onClick={save} disabled={busy}>
                                {busy ? 'Saving…' : 'Save progress'}
                            </button>
                            <button
                                type="button" className="btn btn-primary"
                                onClick={complete}
                                disabled={busy || totals.undecided > 0}
                                title={totals.undecided > 0
                                    ? `${totals.undecided} subject(s) still need a decision`
                                    : undefined}
                            >
                                {busy ? 'Working…' : 'Complete evaluation'}
                            </button>
                        </>
                    ))}
                </footer>
            </div>
        </div>,
        document.body
    );
}
