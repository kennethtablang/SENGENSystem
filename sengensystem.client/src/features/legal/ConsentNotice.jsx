import { useEffect, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import { readItem, writeItem } from '../shell/storage';
import { useModalFocus } from '../shell/useModalFocus';
import { POLICY_VERSION, PRIVACY_URL, TERMS_URL, externalLinkProps } from './policy';
import './legal.css';

const STORAGE_KEY = 'sengen.consent';

/* Whether this browser already agreed to the *current* documents. The record carries the version
   agreed to, so publishing new terms (a new POLICY_VERSION) asks everyone again rather than reusing
   consent given to text they never saw. */
function hasAgreed() {
    try {
        const saved = JSON.parse(readItem(STORAGE_KEY) ?? 'null');
        return saved?.version === POLICY_VERSION;
    } catch {
        return false;
    }
}

/* Shown on the first visit to any page, signed in or not. It asks for agreement, so it can't be
   dismissed by Esc or by clicking outside, only by agreeing. Someone who doesn't agree simply
   doesn't proceed, which the notice says. The full documents open in a new tab and are served
   outside the app, so this notice never covers them. */
function ConsentDialog({ onAgree }) {
    const dialogRef = useModalFocus({ onEscape: () => {} });
    const agreeRef = useRef(null);

    useEffect(() => {
        document.body.style.overflow = 'hidden';
        // The focus hook lands on the first control, which here is the Terms link in the text.
        // Start on the one action instead; Tab still reaches both links.
        agreeRef.current?.focus();
        return () => { document.body.style.overflow = ''; };
    }, []);

    return createPortal(
        <div className="modal-overlay consent-overlay" role="presentation">
            <div
                ref={dialogRef}
                className="modal consent-modal"
                role="dialog"
                aria-modal="true"
                aria-labelledby="consent-title"
                aria-describedby="consent-body"
            >
                <header className="modal-head">
                    <h2 id="consent-title">Terms and Privacy</h2>
                </header>

                <div className="modal-body" id="consent-body">
                    <p>
                        Welcome to <strong>SEN-GEN AI</strong>, the enrollment and class scheduling
                        system of STI College Alaminos.
                    </p>
                    <p>
                        By continuing to use this system, you agree to our{' '}
                        <a href={TERMS_URL} {...externalLinkProps}>Terms and Conditions</a> and
                        acknowledge that your personal data will be processed as described in our{' '}
                        <a href={PRIVACY_URL} {...externalLinkProps}>Privacy Policy</a>, in line with
                        the Data Privacy Act of 2012 (Republic Act No. 10173).
                    </p>
                    <p className="consent-note">
                        Both documents open in a new tab. If you do not agree, please close this page.
                    </p>
                </div>

                <footer className="modal-foot">
                    <button ref={agreeRef} type="button" className="btn btn-primary" onClick={onAgree}>
                        I agree
                    </button>
                </footer>
            </div>
        </div>,
        document.body
    );
}

export default function ConsentNotice() {
    const [agreed, setAgreed] = useState(hasAgreed);

    if (agreed) return null;

    function agree() {
        writeItem(STORAGE_KEY, JSON.stringify({ version: POLICY_VERSION, agreedAt: new Date().toISOString() }));
        setAgreed(true);
    }

    return <ConsentDialog onAgree={agree} />;
}

/* "Terms · Privacy" links for page footers. */
export function LegalLinks({ className = '' }) {
    return (
        <nav className={`legal-links ${className}`.trim()} aria-label="Legal">
            <a href={TERMS_URL} {...externalLinkProps}>Terms and Conditions</a>
            <span aria-hidden="true">·</span>
            <a href={PRIVACY_URL} {...externalLinkProps}>Privacy Policy</a>
        </nav>
    );
}
