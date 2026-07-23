import { useState } from 'react';
import SetupModal from '../academic/SetupModal';
import { restoreSubject, restoreCurriculum } from './api';
import { notifySuccess, notifyError } from '../shell/notify';

/* The archive drawer (opened from "Archived (n)" on the curriculum sheet). Nothing here is
   deleted — curricula and subjects are retired instead, so this is where they can be read
   back and restored. Curricula come from the page's already-loaded list; subjects are the
   archived rows across every curriculum, not just the selected one. */

const fmtDate = (iso) => (iso ? new Date(iso).toLocaleDateString(undefined,
    { year: 'numeric', month: 'short', day: 'numeric' }) : '—');

export default function ArchivedModal({ curricula, subjects, onClose, onRestored }) {
    const [busyId, setBusyId] = useState(null);
    const [error, setError] = useState('');

    const archivedCurricula = curricula.filter(c => c.isArchived);
    const archivedSubjects = subjects.filter(s => s.isArchived);
    const labelFor = (curriculumId) =>
        curricula.find(c => c.id === curriculumId)?.programCode ?? 'No curriculum';

    async function restore(kind, record) {
        setError(''); setBusyId(record.id);
        try {
            if (kind === 'curriculum') {
                await restoreCurriculum(record.id);
                notifySuccess(`${record.programCode} curriculum restored.`);
            } else {
                await restoreSubject(record.id);
                notifySuccess(`Subject ${record.code} restored.`);
            }
            onRestored();
        } catch (ex) {
            setError(ex.message);
            notifyError(ex.message);
        } finally {
            setBusyId(null);
        }
    }

    const isEmpty = archivedCurricula.length === 0 && archivedSubjects.length === 0;

    return (
        <SetupModal
            title="Archived items"
            onClose={onClose}
            className="modal-lg"
            footer={<button type="button" className="btn btn-ghost" onClick={onClose}>Close</button>}
        >
            {error && <div className="alert">{error}</div>}

            {isEmpty ? (
                <p className="arch-empty">
                    Nothing is archived. Retiring a curriculum or subject keeps its history here,
                    where it can be restored at any time.
                </p>
            ) : (
                <>
                    <section className="arch-section">
                        <h4 className="arch-head">
                            Curricula <span className="chip chip-muted">{archivedCurricula.length}</span>
                        </h4>
                        {archivedCurricula.length === 0 ? (
                            <p className="arch-empty">No archived curricula.</p>
                        ) : (
                            <ul className="arch-list">
                                {archivedCurricula.map(c => (
                                    <li key={c.id}>
                                        <span className="arch-main">
                                            <strong className="arch-code">{c.programCode}</strong>
                                            <span className="arch-title">{c.programName}</span>
                                        </span>
                                        <span className="arch-meta">
                                            {c.subjectCount} subject{c.subjectCount === 1 ? '' : 's'}
                                            {' · archived '}{fmtDate(c.archivedAtUtc)}
                                            {c.archiveReason ? ` — ${c.archiveReason}` : ''}
                                        </span>
                                        <button
                                            type="button"
                                            className="btn btn-ghost btn-sm"
                                            disabled={busyId === c.id}
                                            onClick={() => restore('curriculum', c)}
                                        >
                                            Restore
                                        </button>
                                    </li>
                                ))}
                            </ul>
                        )}
                    </section>

                    <section className="arch-section">
                        <h4 className="arch-head">
                            Subjects <span className="chip chip-muted">{archivedSubjects.length}</span>
                        </h4>
                        {archivedSubjects.length === 0 ? (
                            <p className="arch-empty">No archived subjects.</p>
                        ) : (
                            <ul className="arch-list">
                                {archivedSubjects.map(s => (
                                    <li key={s.id}>
                                        <span className="arch-main">
                                            <strong className="arch-code">{s.code}</strong>
                                            <span className="arch-title">{s.title}</span>
                                        </span>
                                        <span className="arch-meta">
                                            {labelFor(s.curriculumId)} · Year {s.yearLevel} · {s.units}u
                                            {' · archived '}{fmtDate(s.archivedAtUtc)}
                                            {s.archiveReason ? ` — ${s.archiveReason}` : ''}
                                        </span>
                                        <button
                                            type="button"
                                            className="btn btn-ghost btn-sm"
                                            disabled={busyId === s.id}
                                            onClick={() => restore('subject', s)}
                                        >
                                            Restore
                                        </button>
                                    </li>
                                ))}
                            </ul>
                        )}
                    </section>
                </>
            )}
        </SetupModal>
    );
}
