import { useState } from 'react';
import { Link, useLocation, useNavigate } from 'react-router-dom';
import { loginAccount } from './api';
import { useAuth } from './useAuth';
import AuthLayout from './AuthLayout';
import PasswordInput from './PasswordInput';
import { notifyError } from '../shell/notify';

function LoginPage() {
    const navigate = useNavigate();
    const location = useLocation();
    const { login } = useAuth();

    const [form, setForm] = useState({ email: '', password: '' });
    const [error, setError] = useState('');
    const [submitting, setSubmitting] = useState(false);

    const justRegistered = location.state?.registered;

    async function handleSubmit(e) {
        e.preventDefault();
        setError('');
        setSubmitting(true);
        try {
            const { token, user } = await loginAccount(form);
            login(token, user);
            navigate('/', { replace: true });
        } catch (err) {
            setError(err.message);
            notifyError(err.message);
        } finally {
            setSubmitting(false);
        }
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
