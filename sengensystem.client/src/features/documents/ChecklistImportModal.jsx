import { Fragment, useMemo, useState } from 'react';
import SetupModal from '../academic/SetupModal';
import { previewChecklistImport, commitChecklistImport } from './api';
import { notifySuccess, notifyError } from '../shell/notify';
import { confirmAction } from '../shell/confirm';
import { confirmsHeavy } from '../settings/prefs';
import { documentStatusLabel, humanize, formatDay } from '../registration/options';

/* FR-DOC-02: import the admission office's requirement checklist from a spreadsheet.

   Two steps over one file. The preview reads every sheet, matches each row to a student, and says
   what would change — nothing is written. The officer then settles the rows the matcher could not
   be sure of, and imports the ones they accept. How a row is matched:

     1. Registration no. or Student ID, when the row has one — exact, and the name is only checked
        to catch an ID typed against the wrong student.
     2. Otherwise the name, compared word by word in any order ("LAST, FIRST MIDDLE" and
        "FIRST MIDDLE LAST" are the same person; accents and middle initials are allowed for).
        One exact fit is matched; a near fit needs a tick; several fits ask the officer to choose.
     3. A name-matched row that carries a Student ID records it on the student — so the next
        import of the same sheet matches on the ID. */

const statusMeta = {
    Matched: { chip: 'chip chip-blue', label: 'Matched' },
    Manual: { chip: 'chip chip-blue', label: 'Linked by you' },
    Probable: { chip: 'chip chip-yellow', label: 'Check match' },
    Conflict: { chip: 'chip chip-yellow', label: 'ID conflict' },
    Ambiguous: { chip: 'chip chip-yellow', label: 'Choose student' },
    Unmatched: { chip: 'chip chip-muted', label: 'Not found' }
};

const filters = [
    { key: 'all', label: 'All rows' },
    { key: 'attention', label: 'Needs review' },
    { key: 'ready', label: 'Ready' },
    { key: 'notfound', label: 'Not found' }
];

function category(row) {
    if (row.status === 'Unmatched') return 'notfound';
    if (row.status === 'Matched' || row.status === 'Manual') return 'ready';
    return 'attention';
}

const outcomeText = {
    Update: null,
    DateUpdate: 'New date received',
    Unchanged: 'Already recorded',
    KeptHigher: 'Kept — SEN-GEN has more',
    NotOffered: 'Not allowed for this paper',
    NotRequired: 'Not required for this student',
    Unrecognised: 'Unreadable value'
};

export default function ChecklistImportModal({ onClose, onImported }) {
    const [file, setFile] = useState(null);
    const [preview, setPreview] = useState(null);
    const [busy, setBusy] = useState(null); // 'preview' | 'commit' | null
    const [error, setError] = useState('');
    const [sheets, setSheets] = useState(null); // null = the server's default selection
    const [overrides, setOverrides] = useState({});
    const [include, setInclude] = useState({});
    const [allowDowngrade, setAllowDowngrade] = useState(false);
    const [recordStudentIds, setRecordStudentIds] = useState(true);
    const [filter, setFilter] = useState('all');
    const [drafts, setDrafts] = useState({});
    const [openRows, setOpenRows] = useState(() => new Set());
    const [editing, setEditing] = useState(() => new Set());

    async function runPreview(next = {}) {
        const target = next.file ?? file;
        if (!target) return;
        const opts = {
            sheets: next.sheets !== undefined ? next.sheets : sheets,
            overrides: next.overrides ?? overrides,
            allowDowngrade: next.allowDowngrade ?? allowDowngrade
        };
        setBusy('preview');
        setError('');
        try {
            const data = await previewChecklistImport(target, opts);
            setPreview(data);
            if (opts.sheets == null) setSheets(data.sheets.filter(s => s.selected).map(s => s.name));
            // Keep the officer's ticks; rows seen for the first time (or just re-linked) take the
            // matcher's default.
            setInclude(prev => {
                const merged = {};
                for (const row of data.rows) {
                    merged[row.key] = row.key in prev && !next.reset?.includes(row.key)
                        ? prev[row.key] && !!row.student
                        : row.includeByDefault;
                }
                return merged;
            });
        } catch (err) {
            setError(err.message);
        } finally {
            setBusy(null);
        }
    }

    function chooseFile(f) {
        if (!f) return;
        setFile(f);
        setPreview(null);
        setSheets(null);
        setOverrides({});
        setInclude({});
        setDrafts({});
        setOpenRows(new Set());
        setEditing(new Set());
        setFilter('all');
        runPreview({ file: f, sheets: null, overrides: {} });
    }

    function toggleSheet(name) {
        const nextSheets = sheets.includes(name) ? sheets.filter(s => s !== name) : [...sheets, name];
        setSheets(nextSheets);
        runPreview({ sheets: nextSheets });
    }

    function link(key, value) {
        const nextOverrides = { ...overrides };
        if (value) nextOverrides[key] = value; else delete nextOverrides[key];
        setOverrides(nextOverrides);
        setEditing(prev => { const s = new Set(prev); s.delete(key); return s; });
        runPreview({ overrides: nextOverrides, reset: [key] });
    }

    function toggleSet(setter, key) {
        setter(prev => {
            const s = new Set(prev);
            if (s.has(key)) s.delete(key); else s.add(key);
            return s;
        });
    }

    const rows = useMemo(() => preview?.rows ?? [], [preview]);
    const counts = useMemo(() => {
        const c = { all: rows.length, attention: 0, ready: 0, notfound: 0 };
        for (const r of rows) c[category(r)]++;
        return c;
    }, [rows]);
    const visible = filter === 'all' ? rows : rows.filter(r => category(r) === filter);

    const chosen = rows.filter(r => include[r.key] && r.student);
    const chosenUpdates = chosen.reduce((n, r) => n + r.updateCount, 0);
    const chosenIds = recordStudentIds ? chosen.filter(r => r.studentIdToRecord).length : 0;
    const allReadyChecked = rows.filter(r => r.student).every(r => include[r.key]);

    function setAll(value, predicate = () => true) {
        setInclude(prev => {
            const next = { ...prev };
            for (const r of rows) if (r.student && predicate(r)) next[r.key] = value;
            return next;
        });
    }

    async function commit() {
        if (chosen.length === 0) return;
        const unsure = chosen.filter(r => r.status === 'Probable' || r.status === 'Conflict').length;
        if (confirmsHeavy() || unsure > 0) {
            const ok = await confirmAction({
                title: `Import ${chosen.length} row${chosen.length === 1 ? '' : 's'}?`,
                message: `This records up to ${chosenUpdates} paper${chosenUpdates === 1 ? '' : 's'} on the students' checklists`
                    + (chosenIds > 0 ? ` and saves ${chosenIds} Student ID${chosenIds === 1 ? '' : 's'}` : '')
                    + '. Each change is logged against your account.'
                    + (unsure > 0 ? ` ${unsure} of the rows were matched by a similar name or have an ID conflict — make sure you checked them.` : ''),
                confirmLabel: 'Import'
            });
            if (!ok) return;
        }
        setBusy('commit');
        setError('');
        try {
            const result = await commitChecklistImport(file, {
                sheets, overrides, allowDowngrade, recordStudentIds,
                include: chosen.map(r => r.key)
            });
            const text = `Imported ${result.rowsApplied} row(s): ${result.papersUpdated} paper(s) updated for `
                + `${result.studentsUpdated} student(s)`
                + (result.studentIdsRecorded > 0 ? `, ${result.studentIdsRecorded} Student ID(s) recorded` : '') + '.';
            notifySuccess(text);
            onImported?.(text);
            onClose();
        } catch (err) {
            setError(err.message);
            notifyError(err.message);
        } finally {
            setBusy(null);
        }
    }

    const footer = (
        <>
            {preview && (
                <span className="dimp-foot-summary setup-foot-spacer">
                    <strong>{chosen.length}</strong> row{chosen.length === 1 ? '' : 's'} selected ·{' '}
                    <strong>{chosenUpdates}</strong> paper update{chosenUpdates === 1 ? '' : 's'}
                    {chosenIds > 0 && <> · <strong>{chosenIds}</strong> Student ID{chosenIds === 1 ? '' : 's'}</>}
                </span>
            )}
            <button type="button" className="btn btn-ghost" onClick={onClose}>Cancel</button>
            <button
                type="button" className="btn btn-primary"
                disabled={!preview || chosen.length === 0 || busy !== null}
                onClick={commit}
            >
                {busy === 'commit' ? 'Importing…' : `Import ${chosen.length} row${chosen.length === 1 ? '' : 's'}`}
            </button>
        </>
    );

    return (
        <SetupModal title="Import requirement checklist" onClose={onClose} footer={footer} className="dimp-modal">
            <div className="dimp-pick">
                <div>
                    <p className="dimp-lead">
                        Upload the admission office's workbook (.xlsx) or a .csv. Each row is matched to a student by
                        <strong> Registration no.</strong> or <strong>Student ID</strong> when it has one, otherwise by
                        <strong> name</strong>. Nothing is saved until you press Import.
                    </p>
                    {preview && <p className="dimp-scope">{preview.scope}</p>}
                </div>
                <div className="dimp-file">
                    <input
                        id="dimp-file" type="file" accept=".xlsx,.csv"
                        className="dimp-file-input" disabled={busy !== null}
                        onChange={e => { chooseFile(e.target.files?.[0]); e.target.value = ''; }}
                    />
                    <label htmlFor="dimp-file" className="btn">
                        {file ? 'Choose another file' : 'Choose file'}
                    </label>
                    {file && <span className="dimp-file-name" title={file.name}>{file.name}</span>}
                </div>
            </div>

            {error && <div className="alert" role="alert">{error}</div>}
            {busy === 'preview' && <p className="reg-empty">Reading the file and matching students…</p>}

            {preview && (
                <>
                    <section className="dimp-sheets" aria-label="Sheets">
                        <h4>Sheets</h4>
                        <div className="dimp-sheet-list">
                            {preview.sheets.map(s => (
                                <label
                                    key={s.name}
                                    className={`dimp-sheet${s.importable ? '' : ' is-disabled'}`}
                                    title={s.importable
                                        ? s.columns.filter(c => c.requirementCode).map(c => `${c.header.trim()} → ${c.requirementLabel}`).join('\n')
                                        : s.reason}
                                >
                                    <input
                                        type="checkbox"
                                        disabled={!s.importable || busy !== null}
                                        checked={!!sheets?.includes(s.name)}
                                        onChange={() => toggleSheet(s.name)}
                                    />
                                    <span>{s.name.trim() || '(unnamed)'}</span>
                                    <small>
                                        {s.importable
                                            ? `${s.rowCount} rows · ${s.columns.filter(c => c.requirementCode).length} papers`
                                            : s.visible ? 'not a checklist' : 'hidden, empty'}
                                    </small>
                                </label>
                            ))}
                        </div>
                        {preview.truncated && (
                            <p className="dimp-warn">A sheet has more than 5,000 rows — only the first 5,000 are read.</p>
                        )}
                    </section>

                    <div className="dimp-toolbar">
                        <div className="dimp-tabs" role="tablist" aria-label="Filter rows">
                            {filters.map(f => (
                                <button
                                    key={f.key} type="button" role="tab" aria-selected={filter === f.key}
                                    className={`dimp-tab${filter === f.key ? ' is-active' : ''}`}
                                    onClick={() => setFilter(f.key)}
                                >
                                    {f.label} <span>{counts[f.key]}</span>
                                </button>
                            ))}
                        </div>
                        <div className="dimp-options">
                            <label className="req-check">
                                <input
                                    type="checkbox" checked={allowDowngrade} disabled={busy !== null}
                                    onChange={e => { setAllowDowngrade(e.target.checked); runPreview({ allowDowngrade: e.target.checked }); }}
                                />
                                Allow downgrades
                            </label>
                            <label className="req-check">
                                <input
                                    type="checkbox" checked={recordStudentIds}
                                    onChange={e => setRecordStudentIds(e.target.checked)}
                                />
                                Save Student IDs from the file
                            </label>
                        </div>
                    </div>

                    {rows.length === 0 ? (
                        <p className="reg-empty">No student rows in the selected sheets.</p>
                    ) : (
                        <div className="dimp-table-wrap">
                            <table className="reg-table dimp-table">
                                <thead>
                                    <tr>
                                        <th className="dimp-col-check">
                                            <input
                                                type="checkbox" aria-label="Select every linked row"
                                                checked={allReadyChecked}
                                                onChange={e => setAll(e.target.checked)}
                                            />
                                        </th>
                                        <th>In the file</th>
                                        <th>Matched student</th>
                                        <th>Changes</th>
                                    </tr>
                                </thead>
                                <tbody>
                                    {visible.map(row => (
                                        <ImportRow
                                            key={row.key}
                                            row={row}
                                            checked={!!include[row.key]}
                                            onCheck={v => setInclude(prev => ({ ...prev, [row.key]: v }))}
                                            open={openRows.has(row.key)}
                                            onToggleOpen={() => toggleSet(setOpenRows, row.key)}
                                            editing={editing.has(row.key)}
                                            onToggleEdit={() => toggleSet(setEditing, row.key)}
                                            draft={drafts[row.key] ?? ''}
                                            onDraft={v => setDrafts(prev => ({ ...prev, [row.key]: v }))}
                                            overridden={row.key in overrides}
                                            onLink={value => link(row.key, value)}
                                            busy={busy !== null}
                                            recordStudentIds={recordStudentIds}
                                        />
                                    ))}
                                </tbody>
                            </table>
                        </div>
                    )}
                    {counts.attention > 0 && (
                        <div className="dimp-bulk">
                            <button
                                type="button" className="btn btn-sm btn-ghost"
                                onClick={() => setAll(true, r => r.status === 'Probable')}
                            >
                                Accept all similar-name matches
                            </button>
                            <span>Check them first — a similar name can be a different student.</span>
                        </div>
                    )}
                </>
            )}
        </SetupModal>
    );
}

function ImportRow({
    row, checked, onCheck, open, onToggleOpen, editing, onToggleEdit, draft, onDraft,
    overridden, onLink, busy, recordStudentIds
}) {
    const meta = statusMeta[row.status] ?? statusMeta.Unmatched;
    const s = row.student;
    const showPicker = !s || editing || row.status === 'Ambiguous' || row.status === 'Conflict';
    const updates = row.changes.filter(c => c.outcome === 'Update' || c.outcome === 'DateUpdate');
    const skipped = row.changes.filter(c => !['Update', 'DateUpdate', 'Unchanged'].includes(c.outcome));

    return (
        <Fragment>
            <tr className={`dimp-row is-${category(row)}`}>
                <td className="dimp-col-check">
                    <input
                        type="checkbox"
                        aria-label={`Import row ${row.row} of ${row.sheet}`}
                        disabled={!s}
                        checked={checked && !!s}
                        onChange={e => onCheck(e.target.checked)}
                    />
                </td>
                <td>
                    <div className="dimp-file-cell">
                        <strong>{row.nameInFile || '(no name)'}</strong>
                        <span className="dimp-meta">
                            {row.sheet.trim()} · row {row.row}
                            {row.studentIdInFile && <> · ID <span className="reg-mono">{row.studentIdInFile}</span></>}
                            {row.registrationNumberInFile && <> · <span className="reg-mono">{row.registrationNumberInFile}</span></>}
                            {row.programInFile && <> · {row.programInFile}</>}
                        </span>
                    </div>
                </td>
                <td>
                    <div className="dimp-match">
                        <span className={meta.chip}>{meta.label}</span>
                        {s && (
                            <div className="dimp-student">
                                <strong>{s.fullName}</strong>
                                <span className="dimp-meta">
                                    <span className="reg-mono">{s.studentNumber}</span>
                                    {s.officialStudentNumber ? <> · ID <span className="reg-mono">{s.officialStudentNumber}</span></> : ' · no Student ID'}
                                    {' · '}{s.program} · {humanize(s.studentType)}
                                    {row.matchedBy && <> · by {row.matchedBy.toLowerCase()}</>}
                                </span>
                                {row.studentIdToRecord && recordStudentIds && (
                                    <span className="dimp-learn">Will save Student ID {row.studentIdToRecord}</span>
                                )}
                            </div>
                        )}
                        {row.notes.map(n => <span key={n} className="dimp-note">{n}</span>)}
                        {row.alsoInRows.length > 0 && (
                            <span className="dimp-note">
                                Also on {row.alsoInRows.map(k => k.replace('!', ' row ')).join(', ')} — the furthest status of each paper is kept.
                            </span>
                        )}

                        {showPicker && (
                            <div className="dimp-picker">
                                {row.candidates.length > 0 && (
                                    <select
                                        aria-label="Link to a student"
                                        value=""
                                        disabled={busy}
                                        onChange={e => e.target.value && onLink(e.target.value)}
                                    >
                                        <option value="">{s ? 'Link to someone else…' : 'Link to…'}</option>
                                        {row.candidates.map(c => (
                                            <option key={c.registrationId} value={c.registrationId}>
                                                {c.fullName} · {c.studentNumber} · {c.program} · {c.fit}
                                            </option>
                                        ))}
                                    </select>
                                )}
                                <form
                                    className="dimp-manual"
                                    onSubmit={e => { e.preventDefault(); if (draft.trim()) onLink(draft.trim()); }}
                                >
                                    <input
                                        type="text" value={draft} disabled={busy}
                                        placeholder="Registration no. or Student ID"
                                        aria-label="Registration number or Student ID to link"
                                        onChange={e => onDraft(e.target.value)}
                                    />
                                    <button type="submit" className="btn btn-sm" disabled={busy || !draft.trim()}>Link</button>
                                </form>
                            </div>
                        )}
                        <div className="dimp-row-actions">
                            {s && !showPicker && (
                                <button type="button" className="dimp-link" onClick={onToggleEdit}>Change</button>
                            )}
                            {s && editing && (
                                <button type="button" className="dimp-link" onClick={onToggleEdit}>Keep this match</button>
                            )}
                            {overridden && (
                                <button type="button" className="dimp-link" disabled={busy} onClick={() => onLink(null)}>
                                    Undo my link
                                </button>
                            )}
                        </div>
                    </div>
                </td>
                <td>
                    <button
                        type="button" className="dimp-changes" onClick={onToggleOpen} aria-expanded={open}
                        disabled={row.changes.length === 0}
                    >
                        {!s ? `${row.changes.length} value${row.changes.length === 1 ? '' : 's'} in the row`
                            : updates.length === 0 ? 'Nothing new'
                                : `${updates.length} update${updates.length === 1 ? '' : 's'}`}
                        {s && skipped.length > 0 && <span className="dimp-skipped"> · {skipped.length} skipped</span>}
                        {row.changes.length > 0 && <span aria-hidden="true" className="dimp-caret">{open ? '▴' : '▾'}</span>}
                    </button>
                </td>
            </tr>
            {open && (
                <tr className="dimp-detail">
                    <td />
                    <td colSpan={3}>
                        <ul className="dimp-change-list">
                            {row.changes.map(c => (
                                <li
                                    key={c.requirementCode}
                                    className={`is-${c.outcome === 'DateUpdate' ? 'update' : c.outcome.toLowerCase()}`}
                                >
                                    <span className="dimp-change-label">{c.label}</span>
                                    <span className="dimp-change-raw" title={`Column “${c.header}”`}>“{c.raw}”</span>
                                    <span className="dimp-change-move">
                                        {c.outcome === 'DateUpdate' ? (
                                            <>Received {c.currentDate ? `${formatDay(c.currentDate)} → ` : ''}{formatDay(c.proposedDate)}</>
                                        ) : (
                                            <>
                                                {c.current && <>{documentStatusLabel(c.current)} → </>}
                                                {c.proposed ? documentStatusLabel(c.proposed) : '—'}
                                                {c.proposedDate && c.outcome === 'Update' && <> · {formatDay(c.proposedDate)}</>}
                                            </>
                                        )}
                                    </span>
                                    {(outcomeText[c.outcome] || c.note) && (
                                        <span className="dimp-change-note">
                                            {[outcomeText[c.outcome], c.note].filter(Boolean).join(' — ')}
                                        </span>
                                    )}
                                </li>
                            ))}
                        </ul>
                    </td>
                </tr>
            )}
        </Fragment>
    );
}
