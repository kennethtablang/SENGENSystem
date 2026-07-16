import { useCallback, useEffect, useRef, useState } from 'react';
import { createPortal } from 'react-dom';

// State lives in the dialog, which mounts fresh every time the modal
// opens — so "read to the end" is required on every viewing.
function TermsDialog({ onClose, onAgree }) {
    const bodyRef = useRef(null);
    const [readToEnd, setReadToEnd] = useState(false);

    const checkScroll = useCallback(() => {
        const el = bodyRef.current;
        if (el && el.scrollTop + el.clientHeight >= el.scrollHeight - 8) {
            setReadToEnd(true);
        }
    }, []);

    useEffect(() => {
        // In case the terms fit without scrolling on very tall screens.
        const raf = requestAnimationFrame(checkScroll);

        const onKey = e => {
            if (e.key === 'Escape') onClose();
        };
        window.addEventListener('keydown', onKey);
        document.body.style.overflow = 'hidden';
        return () => {
            cancelAnimationFrame(raf);
            window.removeEventListener('keydown', onKey);
            document.body.style.overflow = '';
        };
    }, [onClose, checkScroll]);

    // Rendered through a portal to <body> so the fixed overlay is positioned against the viewport
    // — not trapped inside any transformed/animated ancestor (e.g. the SIS form's entrance animation).
    return createPortal(
        <div className="modal-overlay" onClick={onClose} role="presentation">
            <div
                className="modal"
                role="dialog"
                aria-modal="true"
                aria-labelledby="terms-title"
                onClick={e => e.stopPropagation()}
            >
                <header className="modal-head">
                    <h2 id="terms-title">Terms and Conditions</h2>
                    <button type="button" className="modal-close" onClick={onClose} aria-label="Close">
                        ×
                    </button>
                </header>

                <div className="modal-body" ref={bodyRef} onScroll={checkScroll}>
                    <h3>1. Acceptance</h3>
                    <p>
                        By creating a SEN-GEN account you agree to these terms and conditions
                        governing the use of the Student Enrollment and Generative Scheduling
                        System of STI College Alaminos. If you do not agree, do not proceed
                        with registration.
                    </p>

                    <h3>2. Collection of personal data</h3>
                    <p>
                        SEN-GEN collects the personal information you provide during account
                        registration and enrollment — including your name, email address,
                        Student Information Sheet details, and enrollment documents such as
                        Form 137, your birth certificate, and certificate of good moral
                        character. Collection and processing are done in accordance with the
                        Data Privacy Act of 2012 (Republic Act No. 10173) and its implementing
                        rules and regulations.
                    </p>

                    <h3>3. Purpose of processing</h3>
                    <p>
                        Your data is processed solely for legitimate academic administrative
                        purposes: verifying your enrollment requirements, registering you as a
                        student, managing your subject enlistment, generating class schedules,
                        and sending you enrollment-related notifications by email.
                    </p>

                    <h3>4. Access to your data</h3>
                    <p>
                        Access is limited by role: only the Admission Officer, Registrar,
                        Academic Head, and School Administrator of STI College Alaminos can
                        view the records relevant to their function. Your data is not shared
                        with third parties and is not used for marketing.
                    </p>

                    <h3>5. Retention and security</h3>
                    <p>
                        Records are kept only as long as required by the school's academic
                        records policy and applicable regulations. Passwords are stored in
                        hashed form and all access to the system is logged.
                    </p>

                    <h3>6. Your responsibilities</h3>
                    <p>
                        You confirm that the information you provide is true, accurate, and
                        complete. Supplying false information may void your enrollment. You
                        are responsible for keeping your password confidential and for all
                        activity performed under your account.
                    </p>

                    <h3>7. Acknowledgment</h3>
                    <p>
                        The date and time of your agreement are recorded together with your
                        registration as proof of this acknowledgment. For privacy concerns,
                        contact the Registrar's Office of STI College Alaminos.
                    </p>
                </div>

                <footer className="modal-foot">
                    {!readToEnd && <span className="modal-hint">Scroll to the end to enable Agree.</span>}
                    <button type="button" className="btn btn-ghost" onClick={onClose}>
                        Close
                    </button>
                    <button type="button" className="btn btn-primary" disabled={!readToEnd} onClick={onAgree}>
                        I have read and agree
                    </button>
                </footer>
            </div>
        </div>,
        document.body
    );
}

function TermsModal({ open, onClose, onAgree }) {
    if (!open) return null;
    return <TermsDialog onClose={onClose} onAgree={onAgree} />;
}

export default TermsModal;
