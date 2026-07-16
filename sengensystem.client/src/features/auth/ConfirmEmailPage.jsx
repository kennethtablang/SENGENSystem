import { useEffect, useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { confirmEmailChange, fetchCurrentUser } from './api';
import { useAuth } from './useAuth';
import AuthLayout from './AuthLayout';

/* Landing page for the email-change confirmation link (opened from the new mailbox). */
function ConfirmEmailPage() {
    const [params] = useSearchParams();
    const token = params.get('token') ?? '';
    const { user, updateUser } = useAuth();

    const [state, setState] = useState({ status: token ? 'working' : 'error', message: token ? '' : 'This link is incomplete. Open it from the confirmation email again.' });

    useEffect(() => {
        if (!token) return undefined;
        let live = true;
        const run = setTimeout(async () => {
            try {
                const { message } = await confirmEmailChange(token);
                // If the owner is signed in on this device, refresh their session's email.
                const me = await fetchCurrentUser().catch(() => null);
                if (live && me) updateUser(me);
                if (live) setState({ status: 'done', message });
            } catch (err) {
                if (live) setState({ status: 'error', message: err.message });
            }
        }, 0);
        return () => {
            live = false;
            clearTimeout(run);
        };
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [token]);

    return (
        <AuthLayout>
            <div className="auth-card">
                <h1>Confirm email change</h1>
                <p className="auth-subtitle">SEN-GEN account security</p>

                {state.status === 'working' && <p>Confirming your new email address…</p>}
                {state.status === 'done' && <div className="alert alert-success">{state.message}</div>}
                {state.status === 'error' && <div className="alert">{state.message}</div>}

                <p className="auth-switch" style={{ marginTop: '1rem' }}>
                    {user
                        ? <Link to="/profile">Back to Profile settings</Link>
                        : <Link to="/login">Go to sign in</Link>}
                </p>
            </div>
        </AuthLayout>
    );
}

export default ConfirmEmailPage;
