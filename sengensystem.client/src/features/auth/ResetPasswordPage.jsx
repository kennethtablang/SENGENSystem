import { useState } from 'react';
import { Link, useNavigate, useSearchParams } from 'react-router-dom';
import { resetPassword } from './api';
import AuthLayout from './AuthLayout';
import PasswordInput from './PasswordInput';
import { notifyError, notifySuccess } from '../shell/notify';

function ResetPasswordPage() {
    const navigate = useNavigate();
    const [params] = useSearchParams();
    const email = params.get('email') ?? '';
    const token = params.get('token') ?? '';

    const [pw, setPw] = useState({ next: '', confirm: '' });
    const [error, setError] = useState('');
    const [fieldErrors, setFieldErrors] = useState({});
    const [submitting, setSubmitting] = useState(false);

    const linkBroken = !email || !token;

    async function handleSubmit(e) {
        e.preventDefault();
        setError('');
        setFieldErrors({});
        if (pw.next !== pw.confirm) {
            setFieldErrors({ confirm: ['Passwords do not match.'] });
            return;
        }
        setSubmitting(true);
        try {
            const { message } = await resetPassword({ email, token, newPassword: pw.next });
            notifySuccess(message);
            navigate('/login', { replace: true });
        } catch (err) {
            setError(err.message);
            setFieldErrors(err.fieldErrors || {});
            notifyError(err.message);
        } finally {
            setSubmitting(false);
        }
    }

    return (
        <AuthLayout>
            <form className="auth-card" onSubmit={handleSubmit} noValidate>
                <h1>Choose a new password</h1>
                <p className="auth-subtitle">
                    {linkBroken
                        ? 'This link is incomplete.'
                        : <>Resetting the password for <strong>{email}</strong>.</>}
                </p>

                {error && <div className="alert">{error}</div>}

                {linkBroken ? (
                    <p className="auth-switch">
                        Open the link from your email again, or <Link to="/forgot-password">request a new one</Link>.
                    </p>
                ) : (
                    <>
                        <div className="field">
                            <label htmlFor="rp-next">New password</label>
                            <PasswordInput
                                id="rp-next"
                                autoComplete="new-password"
                                value={pw.next}
                                onChange={e => setPw(prev => ({ ...prev, next: e.target.value }))}
                            />
                            {fieldErrors.newPassword && <p className="field-error">{fieldErrors.newPassword[0]}</p>}
                        </div>

                        <div className="field">
                            <label htmlFor="rp-confirm">Confirm new password</label>
                            <PasswordInput
                                id="rp-confirm"
                                autoComplete="new-password"
                                value={pw.confirm}
                                onChange={e => setPw(prev => ({ ...prev, confirm: e.target.value }))}
                            />
                            {fieldErrors.confirm && <p className="field-error">{fieldErrors.confirm[0]}</p>}
                        </div>

                        <button className="btn btn-primary btn-block" type="submit" disabled={submitting}>
                            {submitting && <span className="spinner" aria-hidden="true" />}
                            {submitting ? 'Saving…' : 'Set new password'}
                        </button>

                        <p className="auth-switch">
                            At least 8 characters with both letters and digits.
                        </p>
                    </>
                )}
            </form>
        </AuthLayout>
    );
}

export default ResetPasswordPage;
