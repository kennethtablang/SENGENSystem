import { useCallback, useEffect, useState } from 'react';
import { getEnrollmentStage, STAGE_EVENT } from './api';
import StageModal from './StageModal';
import './stage.css';

/* The top-bar banner: which term is active and where it sits in the enrollment cycle.
   Everyone sees it; the Registrar and School Admin get a button that opens the phase
   control. Refreshes when any other mounted copy changes the stage. */

export default function EnrollmentTicker() {
    const [info, setInfo] = useState(null);
    const [open, setOpen] = useState(false);

    const load = useCallback(() => {
        getEnrollmentStage()
            .then(setInfo)
            .catch(() => setInfo(null)); // a banner is never worth breaking the shell over
    }, []);

    useEffect(() => {
        const timer = setTimeout(load, 0);
        window.addEventListener(STAGE_EVENT, load);
        return () => {
            clearTimeout(timer);
            window.removeEventListener(STAGE_EVENT, load);
        };
    }, [load]);

    if (!info) return <div className="shell-ticker" aria-hidden="true" />;

    if (!info.semesterId) {
        return (
            <div className="shell-ticker">
                <span className="tick">Semester <b>None active</b></span>
            </div>
        );
    }

    return (
        <>
            <div className="shell-ticker">
                <span className="tick">Semester <b>{info.semesterName}</b></span>
                {info.canChange ? (
                    <button
                        type="button"
                        className="tick tick-action"
                        onClick={() => setOpen(true)}
                        title={info.nextStageLabel
                            ? `Change the enrollment stage — next: ${info.nextStageLabel}`
                            : 'Change the enrollment stage'}
                    >
                        Enrollment stage <b>{info.stageLabel}</b>
                        <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor"
                            strokeWidth="2.4" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
                            <path d="m9 18 6-6-6-6" />
                        </svg>
                    </button>
                ) : (
                    <span className="tick">Enrollment stage <b>{info.stageLabel}</b></span>
                )}
            </div>

            {open && (
                <StageModal
                    info={info}
                    onClose={() => setOpen(false)}
                    onChanged={load}
                />
            )}
        </>
    );
}
