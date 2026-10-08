import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import {
    listAcademicRecords, getAcademicRecord, saveAcademicRecord,
    importAcademicRecords, downloadAcademicRecordTemplate
} from './api';
import { notifySuccess, notifyError } from '../shell/notify';
import { formatPHT } from '../registration/options';
import { useServerTable } from '../shell/useServerTable';
import { SortHeader, Pagination } from '../shell/tableControls';
import { useModalFocus } from '../shell/useModalFocus';
import '../registration/registration.css';
import '../evaluation/evaluation.css';
import './academic-records.css';

/* FR-ENL-01/06: what each student has already taken, and how it ended.

   This is the record three rules were resting on nothing without — prerequisites were printed but
   never enforced, a failed subject was never offered again, and year level advanced on the calendar
   rather than on anything earned. It stores verdicts, not grades: SEN-GEN does not grade, and a
   half-built grade book would be worse than none.

   The page is a queue and a sheet, the same shape as the transferee evaluation next to it: find a
   student, work down their curriculum for one term, save. The bulk import is the realistic first
   run — a school adopting SEN-GEN mid-programme has years of this behind it already. */

const VIEWS = [
    { key: 'All', label: 'All students' },
    { key: 'recorded', label: 'Has records' },
    { key: 'none', label: 'No records yet' }
];

const VERDICTS = ['Passed', 'Failed', 'Dropped'];

const statusChip = {
    Passed: 'chip chip-blue',
    Credited: 'chip chip-blue',
    Owed: 'chip chip-yellow',
    NotTaken: 'chip chip-muted'
};

const statusLabel = {
    Passed: 'Passed',
    Credited: 'Credited',
    Owed: 'Still owed',
    NotTaken: 'Not taken'
};

const yearLabel = (n) => (['1st year', '2nd year', '3rd year', '4th year'][n - 1] ?? `Year ${n}`);

/* The verdicts already on file for one term, as the sheet's editable draft. Switching terms
   re-seeds from this rather than carrying the previous term's draft across — otherwise a Registrar
   who picked the wrong term, filled it in, and then corrected the selection would silently write
   the same verdicts into both terms. */
function draftFor(sheet, semesterId) {
    return Object.fromEntries(sheet.subjects.map(s => [
        s.subjectId,
        s.attempts.find(a => a.semesterId === semesterId)?.verdict ?? 'None'
    ]));
}

export default function AcademicRecordsPage() {
    const [rows, setRows] = useState([]);
    const [total, setTotal] = useState(0);
    const [counts, setCounts] = useState({ recordedCount: 0, notRecordedCount: 0 });
    const [loading, setLoading] = useState(true);
    const [view, setView] = useState('All');
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
                const data = await listAcademicRecords({ view, ...table.query });
                if (!active) return;
                setRows(data.students);
                setTotal(data.total);
                // Counted server-side over the whole queue and before the view chip narrows it, so
                // switching to "No records yet" cannot change what the backlog is reported to be.
                setCounts({
                    recordedCount: data.recordedCount,
                    notRecordedCount: data.notRecordedCount
                });
            } catch (err) {
                if (active) setAlert({ kind: 'error', text: err.message });
            } finally {
                if (active) setLoading(false);
            }
        })();
        return () => { active = false; };
    // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [view, table.queryKey, reload]);

    const refreshQueue = useCallback(() => setReload(v => v + 1), []);

    return (
        <div className="reg-page">
            <header className="reg-head">
                <div>
                    <h2>Academic records</h2>
                    <p className="reg-sub">
                        Record how each subject a student sat for ended — passed, failed, or dropped. This is
                        what prerequisites are checked against, what brings a failed subject back into a
                        student’s enlistment list, and what advances their year level. Verdicts only; grades
                        stay with the student-records system.
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
                        <select value={view} onChange={e => setView(e.target.value)}>
                            {VIEWS.map(v => <option key={v.key} value={v.key}>{v.label}</option>)}
                        </select>
                    </label>
                </div>
            </header>

            {alert && (
                <div className={alert.kind === 'success' ? 'alert alert-success' : 'alert'}
                    role={alert.kind === 'success' ? 'status' : 'alert'}>
                    <p>{alert.text}</p>
                </div>
            )}

            <ImportPanel onImported={refreshQueue} />

            {/* The gate falls open for a student with nothing on file, so an empty backlog is not a
                cosmetic detail — it is the difference between the prerequisite rule applying and
                not. Say so where the number is, rather than leaving it to be discovered. */}
            {counts.notRecordedCount > 0 && (
                <div className="arec-note">
                    {counts.notRecordedCount} student{counts.notRecordedCount === 1 ? ' has' : 's have'} no
                    academic record yet. Prerequisites are <strong>not</strong> enforced for them — with
                    nothing on file every prerequisite would read as unmet, which would refuse every
                    continuing student rather than catch the few who genuinely have not met one.
                </div>
            )}

            {loading ? (
                <p className="reg-empty">Loading…</p>
            ) : rows.length === 0 ? (
                <p className="reg-empty">
                    {appliedSearch ? 'No students match your search.' : 'No students in this view.'}
                </p>
            ) : (
                <div className="card reg-table-wrap">
                    <table className="reg-table">
                        <thead>
                            <tr>
                                <SortHeader label="Student no." sortKey="studentNumber" sort={table.sort} onSort={table.toggleSort} />
                                <SortHeader label="Name" sortKey="fullName" sort={table.sort} onSort={table.toggleSort} />
                                <SortHeader label="Program" sortKey="program" sort={table.sort} onSort={table.toggleSort} />
                                <SortHeader label="Year level" sortKey="yearLevel" sort={table.sort} onSort={table.toggleSort} />
                                <SortHeader label="Subjects on file" sortKey="records" sort={table.sort} onSort={table.toggleSort} />
                                <SortHeader label="Units earned" sortKey="earnedUnits" sort={table.sort} onSort={table.toggleSort} />
                                <th></th>
                            </tr>
                        </thead>
                        <tbody>
                            {table.pageRows.map(row => (
                                <tr key={row.registrationId}>
                                    <td className="reg-mono">
                                        {row.officialStudentNumber || row.studentNumber}
                                        {!row.officialStudentNumber && (
                                            <span className="reg-when">Registration no. — student no. not yet issued</span>
                                        )}
                                    </td>
                                    <td><strong>{row.fullName}</strong></td>
                                    <td>{row.program}</td>
                                    <td>{yearLabel(row.yearLevel)}</td>
                                    <td>
                                        {row.recordCount === 0
                                            ? <span className="chip chip-muted">None</span>
                                            : <>{row.recordCount} <span className="reg-when">{row.passedCount} passed</span></>}
                                    </td>
                                    <td>{row.earnedUnits}u</td>
                                    <td className="reg-actions">
                                        <button
                                            className={row.recordCount === 0 ? 'btn btn-primary btn-sm' : 'btn btn-sm'}
                                            type="button"
                                            onClick={() => setOpenId(row.registrationId)}
                                        >
                                            {row.recordCount === 0 ? 'Record' : 'Open'}
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
                {counts.recordedCount} student{counts.recordedCount === 1 ? '' : 's'} with records on file ·
                {' '}{counts.notRecordedCount} with none.
            </p>

            {openId && (
                <RecordSheet
                    registrationId={openId}
                    onClose={() => setOpenId(null)}
                    onChanged={refreshQueue}
                />
            )}
        </div>
    );
}

/* The backfill path. Kept on the page rather than behind its own route because the import and the
   sheet are two ways to do one job, and a Registrar looking at an empty queue should find the bulk
   option without going hunting for it. */
function ImportPanel({ onImported }) {
    const [open, setOpen] = useState(false);
    const [busy, setBusy] = useState(false);
    const [report, setReport] = useState(null);
    const fileRef = useRef(null);

    async function upload(file) {
        if (!file) return;
        setBusy(true);
        setReport(null);
        try {
            const data = await importAcademicRecords(file);
            setReport(data);
            notifySuccess(`${data.loaded} added, ${data.updated} updated.`);
            onImported();
        } catch (err) {
            notifyError(err.message);
            setReport({ error: err.message });
        } finally {
            setBusy(false);
            if (fileRef.current) fileRef.current.value = '';
        }
    }

    return (
        <section className="card arec-import">
            <header>
                <div>
                    <h3>Import from a spreadsheet</h3>
                    <p>
                        One row per subject taken: student number, subject code, term, verdict. A row already
                        on file is updated rather than rejected, so a corrected sheet can simply be re-imported.
                    </p>
                </div>
                <button type="button" className="btn btn-sm btn-ghost" onClick={() => setOpen(o => !o)}>
                    {open ? 'Hide' : 'Import'}
                </button>
            </header>

            {open && (
                <div className="arec-import-body">
                    <button
                        type="button" className="btn btn-sm btn-ghost" disabled={busy}
                        onClick={() => downloadAcademicRecordTemplate().catch(err => notifyError(err.message))}
                    >
                        Download template (.xlsx)
                    </button>
                    <input
                        ref={fileRef}
                        type="file"
                        accept=".xlsx"
                        disabled={busy}
                        onChange={e => upload(e.target.files?.[0])}
                    />
                    {busy && <span className="reg-when">Importing…</span>}

                    {report?.error && <div className="alert" role="alert">{report.error}</div>}
                    {report && !report.error && (
                        <div className="arec-report">
                            <p>
                                <strong>{report.loaded}</strong> added ·{' '}
                                <strong>{report.updated}</strong> updated ·{' '}
                                <strong>{report.failed}</strong> failed of {report.totalRows} rows.
                            </p>
                            {report.failed > 0 && (
                                <ul>
                                    {report.rows
                                        .filter(r => r.outcome === 'Failed')
                                        .slice(0, 20)
                                        .map(r => (
                                            <li key={r.row}>
                                                Row {r.row} ({r.studentNumber || '—'} · {r.subjectCode || '—'}):{' '}
                                                {r.errors.join(' ')}
                                            </li>
                                        ))}
                                </ul>
                            )}
                        </div>
                    )}
                </div>
            )}
        </section>
    );
}

/* One student's whole curriculum, in prospectus order, with every attempt on file — and a term
   selector, because a verdict is always a statement about a particular term. */
function RecordSheet({ registrationId, onClose, onChanged }) {
    const [sheet, setSheet] = useState(null);
    const [error, setError] = useState('');
    const [busy, setBusy] = useState(false);
    const [semesterId, setSemesterId] = useState('');
    // Unsaved verdicts for the selected term, keyed by subject, so a long curriculum can be worked
    // through without a round trip per click.
    const [draft, setDraft] = useState({});

    useEffect(() => {
        let active = true;
        (async () => {
            try {
                const data = await getAcademicRecord(registrationId);
                if (!active) return;
                setSheet(data);
                // Default to the active term: recording the term that just ended is the common case,
                // and it is the one term a Registrar never has to go looking for.
                const term = data.terms.find(t => t.isActive)?.semesterId
                    || data.terms[0]?.semesterId
                    || '';
                setSemesterId(term);
                setDraft(draftFor(data, term));
            } catch (err) {
                if (active) setError(err.message);
            }
        })();
        return () => { active = false; };
    }, [registrationId]);

    // Esc, the focus trap, and returning focus to the row action that opened this sheet.
    const dialogRef = useModalFocus({ onEscape: onClose, enabled: !busy });

    useEffect(() => {
        document.body.style.overflow = 'hidden';
        return () => { document.body.style.overflow = ''; };
    }, []);

    function changeTerm(term) {
        setSemesterId(term);
        if (sheet) setDraft(draftFor(sheet, term));
    }

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

    // What this save would change, so the button can say so and stay disabled when it would do
    // nothing. Compared against what is on file for the selected term, not against the last render.
    const pending = useMemo(() => {
        if (!sheet) return 0;
        return sheet.subjects.reduce((n, s) => {
            const onFile = s.attempts.find(a => a.semesterId === semesterId)?.verdict ?? 'None';
            return n + ((draft[s.subjectId] ?? 'None') === onFile ? 0 : 1);
        }, 0);
    }, [sheet, draft, semesterId]);

    function setVerdict(subjectId, verdict) {
        setDraft(prev => ({ ...prev, [subjectId]: verdict }));
    }

    function setBlock(subjects, verdict) {
        setDraft(prev => {
            const next = { ...prev };
            for (const s of subjects) next[s.subjectId] = verdict;
            return next;
        });
    }

    async function save() {
        setBusy(true);
        try {
            const items = Object.entries(draft).map(([subjectId, verdict]) => ({
                subjectId, verdict, remarks: null
            }));
            const data = await saveAcademicRecord(registrationId, { semesterId, items });
            setSheet(data);
            setDraft(draftFor(data, semesterId));
            notifySuccess('Academic record saved.');
            onChanged();
        } catch (err) {
            setError(err.message);
            notifyError(err.message);
        } finally {
            setBusy(false);
        }
    }

    const termName = sheet?.terms.find(t => t.semesterId === semesterId)?.name ?? '';

    return createPortal(
        <div className="modal-overlay" onClick={() => !busy && onClose()} role="presentation">
            <div
                ref={dialogRef}
                className="modal eval-modal"
                role="dialog" aria-modal="true" aria-label="Academic record"
                onClick={e => e.stopPropagation()}
            >
                <header className="modal-head">
                    <h2>{sheet ? sheet.fullName : 'Academic record'}</h2>
                    <button type="button" className="modal-close" onClick={onClose} aria-label="Close" disabled={busy}>×</button>
                </header>

                <div className="modal-body">
                    {error && <div className="alert" role="alert">{error}</div>}
                    {!sheet ? (
                        <p className="reg-empty">Loading the curriculum…</p>
                    ) : (
                        <>
                            <div className="eval-meta">
                                <span className="reg-mono">{sheet.officialStudentNumber || sheet.studentNumber}</span>
                                <span>·</span>
                                <span>{sheet.program}</span>
                                <span>·</span>
                                <span>{yearLabel(sheet.yearLevel)}</span>
                                {sheet.curriculumName && <><span>·</span><span>{sheet.curriculumName}</span></>}
                            </div>

                            {sheet.subjects.length === 0 ? (
                                <p className="reg-empty">
                                    No curriculum is set up for {sheet.program} yet — add its subjects under
                                    Subjects &amp; curriculum before recording anything.
                                </p>
                            ) : (
                                <>
                                    <div className="eval-stats">
                                        <div><span className="eval-stat-num">{sheet.earnedUnits}</span><span>units earned</span></div>
                                        <div><span className="eval-stat-num">{sheet.passedCount}</span><span>subjects passed</span></div>
                                        <div className={sheet.owedCount > 0 ? 'warn' : undefined}>
                                            <span className="eval-stat-num">{sheet.owedCount}</span><span>still owed</span>
                                        </div>
                                        <div>
                                            <span className="eval-stat-num">
                                                {sheet.derivedYearLevel ? yearLabel(sheet.derivedYearLevel) : '—'}
                                            </span>
                                            <span>year level earned</span>
                                        </div>
                                    </div>

                                    {!sheet.isEnforceable && (
                                        <div className="alert arec-open-gate" role="status">
                                            Nothing is on file for this student yet, so <strong>prerequisites are not
                                            enforced</strong> for them. Record even one term and the check starts
                                            applying — with an empty record every prerequisite reads as unmet, which
                                            would refuse them everything rather than catch a real gap.
                                        </div>
                                    )}

                                    {sheet.derivedYearLevel && sheet.derivedYearLevel !== sheet.yearLevel && (
                                        <div className="alert arec-open-gate" role="status">
                                            Their units earn {yearLabel(sheet.derivedYearLevel)}, but they are
                                            currently set to {yearLabel(sheet.yearLevel)}. The year level is settled
                                            on term activation, where this derivation is the default and can be
                                            overridden.
                                        </div>
                                    )}

                                    <label className="arec-term">
                                        <span>Recording for</span>
                                        <select
                                            value={semesterId}
                                            disabled={busy}
                                            onChange={e => changeTerm(e.target.value)}
                                        >
                                            {sheet.terms.map(t => (
                                                <option key={t.semesterId} value={t.semesterId}>
                                                    {t.name}{t.isActive ? ' — active' : ''}
                                                </option>
                                            ))}
                                        </select>
                                        <small>
                                            A verdict is always about one term. Pick the term the subject was taken
                                            in; a subject retaken later gets a second row rather than replacing the
                                            first.
                                        </small>
                                    </label>

                                    {grouped.map(block => (
                                        <section className="eval-block" key={`${block.yearLevel}-${block.termLabel}`}>
                                            <h4>
                                                {yearLabel(block.yearLevel)} · {block.termLabel}
                                                <span className="eval-block-units">
                                                    {block.subjects.reduce((sum, s) => sum + s.units, 0)}u
                                                </span>
                                                <span className="eval-block-actions">
                                                    <button
                                                        type="button" className="btn btn-sm btn-ghost" disabled={busy}
                                                        onClick={() => setBlock(block.subjects, 'Passed')}
                                                    >All passed</button>
                                                    <button
                                                        type="button" className="btn btn-sm btn-ghost" disabled={busy}
                                                        onClick={() => setBlock(block.subjects, 'None')}
                                                    >Clear</button>
                                                </span>
                                            </h4>
                                            <ul className="eval-list">
                                                {block.subjects.map(s => {
                                                    const chosen = draft[s.subjectId] ?? 'None';
                                                    const others = s.attempts.filter(a => a.semesterId !== semesterId);
                                                    return (
                                                        <li
                                                            key={s.subjectId}
                                                            className={`eval-item arec-item${s.status === 'Owed' ? ' is-undecided' : ''}${s.status === 'Passed' || s.status === 'Credited' ? ' is-credited' : ''}`}
                                                        >
                                                            <div className="eval-item-main">
                                                                <span className="eval-code">{s.code}</span>
                                                                <span className="eval-title">{s.title}</span>
                                                                <span className="eval-units">{s.units}u</span>
                                                                <span className={statusChip[s.status] || 'chip chip-muted'}>
                                                                    {statusLabel[s.status] || s.status}
                                                                </span>
                                                            </div>
                                                            <div className="eval-item-decide">
                                                                <select
                                                                    value={chosen}
                                                                    disabled={busy}
                                                                    aria-label={`Verdict for ${s.code} in ${termName}`}
                                                                    onChange={e => setVerdict(s.subjectId, e.target.value)}
                                                                >
                                                                    <option value="None">— not this term —</option>
                                                                    {VERDICTS.map(v => <option key={v} value={v}>{v}</option>)}
                                                                </select>
                                                            </div>
                                                            {(s.prerequisites.length > 0 || others.length > 0) && (
                                                                <div className="arec-item-meta">
                                                                    {s.prerequisites.length > 0 && (
                                                                        <span className="arec-prereq">
                                                                            Needs {s.prerequisites.join(', ')}
                                                                        </span>
                                                                    )}
                                                                    {others.map(a => (
                                                                        <span key={a.semesterId} className="arec-attempt">
                                                                            {a.verdict} · {a.semesterName}
                                                                            {a.recordedAtUtc && ` · ${formatPHT(a.recordedAtUtc)}`}
                                                                        </span>
                                                                    ))}
                                                                </div>
                                                            )}
                                                        </li>
                                                    );
                                                })}
                                            </ul>
                                        </section>
                                    ))}
                                </>
                            )}
                        </>
                    )}
                </div>

                <footer className="modal-foot">
                    <span className="setup-foot-spacer" />
                    <button type="button" className="btn btn-ghost" onClick={onClose} disabled={busy}>Close</button>
                    {sheet && sheet.subjects.length > 0 && (
                        <button
                            type="button" className="btn btn-primary"
                            onClick={save}
                            disabled={busy || pending === 0 || !semesterId}
                            title={pending === 0 ? 'Nothing has changed for this term' : undefined}
                        >
                            {busy ? 'Saving…' : pending === 0 ? 'Save' : `Save ${pending} change${pending === 1 ? '' : 's'}`}
                        </button>
                    )}
                </footer>
            </div>
        </div>,
        document.body
    );
}
