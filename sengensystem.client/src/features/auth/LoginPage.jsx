import { useState } from 'react';
import { Link, useLocation, useNavigate } from 'react-router-dom';
import { loginAccount, verifyTwoFactor, resendTwoFactor } from './api';
import { useAuth } from './useAuth';
import AuthLayout from './AuthLayout';
import PasswordInput from './PasswordInput';
import { notifyError, notifySuccess } from '../shell/notify';

function LoginPage() {
    const navigate = useNavigate();
    const location = useLocation();
    const { login } = useAuth();

    const [form, setForm] = useState({ email: '', password: '' });
    const [error, setError] = useState('');
    const [submitting, setSubmitting] = useState(false);

    // Two-factor challenge state: set once the password step returns twoFactorRequired.
    const [challengeToken, setChallengeToken] = useState(null);
    const [code, setCode] = useState('');
    const [resending, setResending] = useState(false);

    const justRegistered = location.state?.registered;

    function finish(token, user) {
        login(token, user);
        navigate('/', { replace: true });
    }

    async function handleSubmit(e) {
        e.preventDefault();
        setError('');
        setSubmitting(true);
        try {
            const result = await loginAccount(form);
            if (result.twoFactorRequired) {
                // Password accepted; a code was emailed. Switch to the code-entry step.
                setChallengeToken(result.challengeToken);
                setCode('');
            } else {
                finish(result.token, result.user);
            }
        } catch (err) {
            setError(err.message);
            notifyError(err.message);
        } finally {
            setSubmitting(false);
        }
    }

    async function verifyCode(e) {
        e.preventDefault();
        setError('');
        setSubmitting(true);
        try {
            const { token, user } = await verifyTwoFactor({ challengeToken, code: code.trim() });
            finish(token, user);
        } catch (err) {
            setError(err.message);
            notifyError(err.message);
        } finally {
            setSubmitting(false);
        }
    }

    async function resend() {
        setResending(true);
        setError('');
        try {
            await resendTwoFactor(challengeToken);
            notifySuccess('A new code is on its way.');
        } catch (err) {
            notifyError(err.message);
        } finally {
            setResending(false);
        }
    }

    function backToPassword() {
        setChallengeToken(null);
        setCode('');
        setError('');
    }

    if (challengeToken) {
        return (
            <AuthLayout>
                <form className="auth-card" onSubmit={verifyCode} noValidate>
                    <h1>Enter your code</h1>
                    <p className="auth-subtitle">
                        We emailed a 6-digit sign-in code to <strong>{form.email}</strong>. It expires in 10 minutes.
                    </p>

                    {error && <div className="alert">{error}</div>}

                    <div className="field">
                        <label htmlFor="code">Sign-in code</label>
                        <input
                            id="code"
                            type="text"
                            inputMode="numeric"
                            autoComplete="one-time-code"
                            maxLength={6}
                            placeholder="123456"
                            value={code}
                            onChange={e => setCode(e.target.value.replace(/\D/g, ''))}
                            autoFocus
                            required
                            style={{ letterSpacing: '0.4em', textAlign: 'center', fontSize: '1.2rem' }}
                        />
                    </div>

                    <button className="btn btn-primary btn-block" type="submit" disabled={submitting || code.length < 6}>
                        {submitting && <span className="spinner" aria-hidden="true" />}
                        {submitting ? 'Verifying…' : 'Verify and sign in'}
                    </button>

                    <p className="auth-switch">
                        Didn&rsquo;t get it?{' '}
                        <button type="button" className="link-btn" onClick={resend} disabled={resending}>
                            {resending ? 'Sending…' : 'Resend code'}
                        </button>
                        {' · '}
                        <button type="button" className="link-btn" onClick={backToPassword}>
                            Use a different account
                        </button>
                    </p>
                </form>
            </AuthLayout>
        );
    }

    return (
        <AuthLayout>
            <form className="auth-card" onSubmit={handleSubmit} noValidate>
                <h1>Sign in</h1>
                <p className="auth-subtitle">Use your SEN-GEN account email.</p>

                {justRegistered && (
                    <div className="alert alert-success">
                        Account created. Sign in to continue.
                    </div>
                )}
                {error && <div className="alert">{error}</div>}

                <div className="field">
                    <label htmlFor="email">Email</label>
                    <input
                        id="email"
                        type="email"
                        autoComplete="email"
                        value={form.email}
                        onChange={e => setForm(prev => ({ ...prev, email: e.target.value }))}
                        required
                    />
                </div>

                <div className="field">
                    <label htmlFor="password">Password</label>
                    <PasswordInput
                        id="password"
                        autoComplete="current-password"
                        value={form.password}
                        onChange={e => setForm(prev => ({ ...prev, password: e.target.value }))}
                    />
                    <p style={{ textAlign: 'right', marginTop: '0.4rem', fontSize: '0.82rem' }}>
                        <Link to="/forgot-password">Forgot password?</Link>
                    </p>
                </div>

                <button className="btn btn-primary btn-block" type="submit" disabled={submitting}>
                    {submitting && <span className="spinner" aria-hidden="true" />}
                    {submitting ? 'Signing in…' : 'Sign in'}
                </button>

                <p className="auth-switch">
                    New student? <Link to="/register-sis">Register (SIS)</Link>
                    {' · '}
                    Returning? <Link to="/term-activation">Activate your term</Link>
                </p>
            </form>
        </AuthLayout>
    );
}

export default LoginPage;
