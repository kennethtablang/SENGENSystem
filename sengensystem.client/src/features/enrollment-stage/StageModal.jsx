import { useState } from 'react';
import SetupModal from '../academic/SetupModal';
import { advanceEnrollmentStage, setEnrollmentStage, announceStageChange } from './api';
import { notifySuccess, notifyError } from '../shell/notify';
import { confirmAction } from '../shell/confirm';
import './stage.css';

/* The enrollment-cycle control (Registrar / School Admin). Shows the term's phases as a
   stepper — done, current, upcoming — with one primary action to move to the next phase,
   and a picker for correcting a mis-set stage (including stepping back). */

export default function StageModal({ info, onClose, onChanged }) {
    const [choice, setChoice] = useState(info.stage);
    const [busy, setBusy] = useState(false);
    const [error, setError] = useState('');

    const stages = info.stages ?? [];
    const currentIndex = stages.findIndex(s => s.value === info.stage);
    const isCorrection = choice !== info.stage && choice !== info.nextStage;

    async function run(action) {
        setError(''); setBusy(true);
        try {
            const saved = await action();
            notifySuccess(`${saved.semesterName} is now in ${saved.stageLabel}.`);
            announceStageChange();
            onChanged(saved);
            onClose();
        } catch (ex) {
            setError(ex.message);
            notifyError(ex.message);
            setBusy(false);
        }
    }

    async function advance() {
        const ok = await confirmAction({
            title: `Move to ${info.nextStageLabel}?`,
            message: `${info.semesterName} leaves ${info.stageLabel} and enters ${info.nextStageLabel}. `
                + 'Everyone signed in sees the new stage, and it is recorded in the audit trail.',
            confirmLabel: `Move to ${info.nextStageLabel}`
        });
        if (ok) await run(advanceEnrollmentStage);
    }

    async function apply() {
        const label = stages.find(s => s.value === choice)?.label ?? choice;
        const back = stages.findIndex(s => s.value === choice) < currentIndex;
        const ok = await confirmAction({
            title: `Set the stage to ${label}?`,
            message: back
                ? `This moves ${info.semesterName} back from ${info.stageLabel}. Use it to correct a stage `
                    + 'that was advanced by mistake.'
                : `This jumps ${info.semesterName} straight to ${label}, skipping the stages in between.`,
            confirmLabel: `Set ${label}`
        });
        if (ok) await run(() => setEnrollmentStage(choice));
    }

    const footer = (
        <>
            {isCorrection ? (
                <button type="button" className="btn btn-ghost" disabled={busy} onClick={apply}>
                    Set selected stage
                </button>
            ) : <span className="setup-foot-spacer" />}
            <button type="button" className="btn btn-ghost" onClick={onClose}>Cancel</button>
            {info.nextStage ? (
                <button type="button" className="btn btn-primary" disabled={busy} onClick={advance}>
                    {busy && <span className="spinner" aria-hidden="true" />}
                    Move to {info.nextStageLabel}
                </button>
            ) : (
                <button type="button" className="btn btn-primary" disabled>Final stage reached</button>
            )}
        </>
    );

    return (
        <SetupModal title="Enrollment stage" onClose={onClose} footer={footer}>
            {error && <div className="alert">{error}</div>}

            <p className="stage-lead">
                <strong>{info.semesterName}</strong> is in <strong>{info.stageLabel}</strong>.
                Advancing tells every signed-in user which part of the cycle the school is working through.
            </p>

            <ol className="stage-steps">
                {stages.map((s, i) => {
                    const state = i < currentIndex ? 'is-done' : i === currentIndex ? 'is-current' : '';
                    return (
                        <li key={s.value} className={`stage-step ${state}`.trim()}>
                            <label>
                                <input
                                    type="radio"
                                    name="stage"
                                    value={s.value}
                                    checked={choice === s.value}
                                    onChange={() => setChoice(s.value)}
                                />
                                <span className="stage-mark">{i < currentIndex ? '✓' : i + 1}</span>
                                <span className="stage-body">
                                    <span className="stage-name">{s.label}</span>
                                    <span className="stage-note">{STAGE_NOTE[s.value]}</span>
                                </span>
                                {i === currentIndex && <span className="chip chip-active">Current</span>}
                                {s.value === info.nextStage && <span className="chip chip-yellow">Next</span>}
                            </label>
                        </li>
                    );
                })}
            </ol>
        </SetupModal>
    );
}

const STAGE_NOTE = {
    Preparation: 'Back-office setup — curricula, sections, and the timetable are still being built.',
    DocumentSubmission: 'Admission requirements are being collected and verified.',
    Registration: 'Students file Student Information Sheets; the Registrar confirms them.',
    Enlistment: 'Cleared students reserve seats in published sections.',
    Closed: 'Enrollment has ended for the term; the roster is final.'
};
