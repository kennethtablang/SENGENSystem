// Where the full Terms and Conditions and Privacy Policy live, and which version a visitor agreed to.
//
// The two documents are standalone pages (public/legal/*.html), served outside the app so the
// consent pop-up never covers them. To host them on another website instead (for example the
// school's own policy pages), set VITE_TERMS_URL / VITE_PRIVACY_URL at build time; no code change.
//
// POLICY_VERSION is the effective date of the current documents. Change it whenever either document
// changes: every visitor is then asked to agree again, because they agreed to the old text.

export const POLICY_VERSION = '2026-11-23';

export const TERMS_URL = import.meta.env.VITE_TERMS_URL || '/legal/terms.html';
export const PRIVACY_URL = import.meta.env.VITE_PRIVACY_URL || '/legal/privacy.html';

// Opens in a new tab so the visitor keeps their place in the app; noopener so the policy page can't
// script the app's window if it is ever hosted elsewhere.
export const externalLinkProps = { target: '_blank', rel: 'noopener noreferrer' };
