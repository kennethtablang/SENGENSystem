import { useState } from 'react';
import { createPortal } from 'react-dom';
import { requestEmailChange } from './api';
import { notifyError, notifySuccess } from '../shell/notify';

/* Secure email change: the new address only takes effect after the confirmation
   link sent to it is opened. Portaled to <body> — the profile cards animate with
   transform, which would otherwise trap this fixed overlay. */
function ChangeEmailModal({ currentEmail, onClose }) {
    const [form, setForm] = useState({ newEmail: '', confirmEmail: '' });
    const [fieldErrors, setFieldErrors] = useState({});
    const [error, setError] = useState('');
    const [sentTo, setSentTo] = useState(null);
    const [submitting, setSubmitting] = useState(false);

    const set = field => e => setForm(prev => ({ ...prev, [field]: e.target.value }));

    async function handleSubmit(e) {
        e.preventDefault();
        setError('');
        setFieldErrors({});
        if (form.newEmail.trim().toLowerCase() !== form.confirmEmail.trim().toLowerCase()) {
            setFieldErrors({ confirmEmail: ['The email addresses do not match.'] });
            return;
        }
        setSubmitting(true);
        try {
            const { message, pendingEmail } = await requestEmailChange(form);
            setSentTo(pendingEmail);
            notifySuccess(message);
        } catch (err) {
            setError(err.message);
            setFieldErrors(err.fieldErrors || {});
            notifyError(err.message);
        } finally {
            setSubmitting(false);
        }
    }

    return createPortal(
        <div className="modal-overlay" onClick={e => e.target === e.currentTarget && onClose()}>
            <form className="modal" onSubmit={handleSubmit} noValidate>
                <div className="modal-head">
                    <h2>Change email address</h2>
                    <button type="button" className="modal-close" onClick={onClose} aria-label="Close">×</button>
                </div>

                <div className="modal-body">
                    {sentTo ? (
                        <div className="alert alert-success">
                            Confirmation link sent to <strong>{sentTo}</strong>. Your email changes the
                            moment you open it — until then, keep signing in with your current address.
                        </div>
                    ) : (
                        <>
                            <div className="alert" style={{
                                borderColor: 'rgba(0, 51, 153, 0.3)',
                                background: 'var(--sti-blue-dim)',
                                color: 'var(--sti-blue)'
                            }}>
                                You&rsquo;ll receive a confirmation link at your new email address.
                                The change completes only after you open it.
                            </div>

                            <div className="field">
                                <label>Current email</label>
                                <input type="email" value={currentEmail} disabled />
                            </div>

                            <div className="field">
                                <label htmlFor="ce-new">New email address</label>
                                <input
                                    id="ce-new"
                                    type="email"
                                    autoComplete="email"
                                    value={form.newEmail}
                                    onChange={set('newEmail')}
                                    required
                                />
                                {fieldErrors.newEmail && <p className="field-error">{fieldErrors.newEmail[0]}</p>}
                            </div>

                            <div className="field">
                                <label htmlFor="ce-confirm">Re-enter new email address</label>
                                <input
                                    id="ce-confirm"
                                    type="email"
                                    autoComplete="email"
                                    value={form.confirmEmail}
                                    onChange={set('confirmEmail')}
                                    required
                                />
                                {fieldErrors.confirmEmail && <p className="field-error">{fieldErrors.confirmEmail[0]}</p>}
                            </div>

                            {error && <div className="alert">{error}</div>}
                        </>
                    )}
                </div>

                <div className="modal-foot">
                    <button type="button" className="btn btn-ghost" onClick={onClose}>
                        {sentTo ? 'Close' : 'Cancel'}
                    </button>
                    {!sentTo && (
                        <button className="btn btn-primary" type="submit" disabled={submitting}>
                            {submitting && <span className="spinner" aria-hidden="true" />}
                            {submitting ? 'Sending…' : 'Send Confirmation Email'}
                        </button>
                    )}
                </div>
            </form>
        </div>,
        document.body
    );
}

export default ChangeEmailModal;
