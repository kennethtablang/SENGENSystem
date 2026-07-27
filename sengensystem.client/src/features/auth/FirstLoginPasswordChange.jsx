import { useState } from 'react';
import { changePassword } from '../profile/api';
import { fetchCurrentUser } from './api';
import { useAuth } from './useAuth';
import AuthLayout from './AuthLayout';
import PasswordInput from './PasswordInput';
import { notifyError, notifySuccess } from '../shell/notify';

// Shown in place of the whole app when a student signs in on the temporary password SEN-GEN
// generated from their SIS. They cannot reach anything else until they set their own password
// (mirrors the server's MustChangePassword flag, which the change-password endpoint clears).
function FirstLoginPasswordChange() {
    const { user, updateUser, logout } = useAuth();
    const [form, setForm] = useState({ currentPassword: '', newPassword: '', confirm: '' });
    const [error, setError] = useState('');
    const [submitting, setSubmitting] = useState(false);

    async function handleSubmit(e) {
        e.preventDefault();
        setError('');
        if (form.newPassword !== form.confirm) {
            setError('The new password and its confirmation do not match.');
            return;
        }
        setSubmitting(true);
        try {
            await changePassword({ currentPassword: form.currentPassword, newPassword: form.newPassword });
            // Re-fetch so the cleared MustChangePassword flag lets the app through.
            const fresh = await fetchCurrentUser();
            updateUser(fresh ?? { ...user, mustChangePassword: false });
            notifySuccess('Password updated. Welcome to SEN-GEN.');
        } catch (err) {
            const field = err.fieldErrors?.newPassword?.[0] || err.fieldErrors?.currentPassword?.[0];
            setError(field || err.message);
            notifyError(err.message);
        } finally {
            setSubmitting(false);
        }
    }

    return (
        <AuthLayout>
            <form className="auth-card" onSubmit={handleSubmit} noValidate>
                <h1>Set your password</h1>
                <p className="auth-subtitle">
                    Your account was created from your registration with a temporary password.
                    Choose your own password to continue.
                </p>

                {error && <div className="alert">{error}</div>}

                <div className="field">
                    <label htmlFor="currentPassword">Temporary password</label>
                    <PasswordInput
                        id="currentPassword"
                        autoComplete="current-password"
                        value={form.currentPassword}
                        onChange={e => setForm(prev => ({ ...prev, currentPassword: e.target.value }))}
                    />
                </div>

                <div className="field">
                    <label htmlFor="newPassword">New password</label>
                    <PasswordInput
                        id="newPassword"
                        autoComplete="new-password"
                        value={form.newPassword}
                        onChange={e => setForm(prev => ({ ...prev, newPassword: e.target.value }))}
                    />
                    <p className="field-hint">At least 8 characters, with both letters and digits.</p>
                </div>

                <div className="field">
                    <label htmlFor="confirm">Confirm new password</label>
                    <PasswordInput
                        id="confirm"
                        autoComplete="new-password"
                        value={form.confirm}
                        onChange={e => setForm(prev => ({ ...prev, confirm: e.target.value }))}
                    />
                </div>

                <button className="btn btn-primary btn-block" type="submit" disabled={submitting}>
                    {submitting && <span className="spinner" aria-hidden="true" />}
                    {submitting ? 'Saving…' : 'Set password and continue'}
                </button>

                <p className="auth-switch">
                    <button
                        type="button"
                        onClick={logout}
                        style={{
                            background: 'none', border: 'none', padding: 0, cursor: 'pointer',
                            color: 'var(--sti-blue)', font: 'inherit', textDecoration: 'underline'
                        }}
                    >
                        Sign in as a different user
                    </button>
                </p>
            </form>
        </AuthLayout>
    );
}

export default FirstLoginPasswordChange;
