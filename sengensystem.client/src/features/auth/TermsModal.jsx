import { useCallback, useEffect, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import { useModalFocus } from '../shell/useModalFocus';
import { PRIVACY_URL, TERMS_URL, externalLinkProps } from '../legal/policy';

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

    // Esc, the focus trap, and returning focus to the link that opened this.
    const dialogRef = useModalFocus({ onEscape: onClose });

    useEffect(() => {
        // In case the terms fit without scrolling on very tall screens.
        const raf = requestAnimationFrame(checkScroll);
        document.body.style.overflow = 'hidden';
        return () => {
            cancelAnimationFrame(raf);
            document.body.style.overflow = '';
        };
    }, [checkScroll]);

    // Rendered through a portal to <body> so the fixed overlay is positioned against the viewport
    // — not trapped inside any transformed/animated ancestor (e.g. the SIS form's entrance animation).
    return createPortal(
        <div className="modal-overlay" onClick={onClose} role="presentation">
            <div
                ref={dialogRef}
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
                        SEN-GEN collects the personal information you provide on your Student
                        Information Sheet — your name, birth details, civil status, contact
                        details, address, parents' or guardian's names and numbers, and
                        educational background — and the records of your enrollment. Your
                        admission documents (such as Form 137 and your birth certificate) are
                        handed in on campus; the system records only whether each was submitted
                        and does not store copies. Collection and processing follow the Data
                        Privacy Act of 2012 (Republic Act No. 10173) and its implementing rules.
                    </p>

                    <h3>3. Purpose of processing</h3>
                    <p>
                        Your data is processed solely for legitimate academic administrative
                        purposes: verifying your admission requirements, registering you as a
                        student, evaluating transfer credits, managing your subject enlistment,
                        generating class schedules, and sending you enrollment-related
                        notifications by email. The system's AI scheduling feature arranges
                        sections, rooms, and faculty; it does not use your personal data to
                        profile you or make decisions about you.
                    </p>

                    <h3>4. Access to your data</h3>
                    <p>
                        Access is limited by role: the Admission Officer, Registrar, Academic
                        Head, and administrators of STI College Alaminos see only the records
                        their work requires, and faculty see only class counts. Your data is not
                        sold or used for marketing. It is handled only by the school's hosting
                        and email providers on the school's behalf, and shared with government
                        agencies only when the law requires.
                    </p>

                    <h3>5. Retention and security</h3>
                    <p>
                        Records are kept only as long as required by the school's academic
                        records policy and applicable regulations. Connections are encrypted,
                        passwords are stored in hashed form, repeated failed sign-ins lock the
                        account temporarily, and changes to records are logged.
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
                        registration as proof of this acknowledgment. To access or correct your
                        data, or for privacy concerns, contact the Data Protection Officer through
                        the Registrar's Office of STI College Alaminos.
                    </p>
                    <p>
                        This is a summary. The complete{' '}
                        <a href={TERMS_URL} {...externalLinkProps}>Terms and Conditions</a> and{' '}
                        <a href={PRIVACY_URL} {...externalLinkProps}>Privacy Policy</a> open in a new tab.
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
