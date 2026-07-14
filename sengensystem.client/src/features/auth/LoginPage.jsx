import { useState } from 'react';
import { Link, useLocation, useNavigate } from 'react-router-dom';
import { loginAccount } from './api';
import { useAuth } from './useAuth';
import './auth.css';

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
        } finally {
            setSubmitting(false);
        }
    }

    return (
        <div className="auth-page">
            <form className="auth-card" onSubmit={handleSubmit} noValidate>
                <h1>Sign in</h1>
                <p className="auth-subtitle">
                    SEN-GEN Student Enrollment &amp; Class Scheduling — STI Alaminos
                </p>

                {justRegistered && (
                    <div className="auth-alert auth-alert-success">
                        Account created successfully. You can now sign in.
                    </div>
                )}
                {error && <div className="auth-alert">{error}</div>}

                <div className="auth-field">
                    <label htmlFor="email">Email address</label>
                    <input
                        id="email"
                        type="email"
                        value={form.email}
                        onChange={e => setForm(prev => ({ ...prev, email: e.target.value }))}
                        required
                    />
                </div>

                <div className="auth-field">
                    <label htmlFor="password">Password</label>
                    <input
                        id="password"
                        type="password"
                        value={form.password}
                        onChange={e => setForm(prev => ({ ...prev, password: e.target.value }))}
                        required
                    />
                </div>

                <button className="auth-submit" type="submit" disabled={submitting}>
                    {submitting ? 'Signing in…' : 'Sign in'}
                </button>

                <p className="auth-switch">
                    New student? <Link to="/register">Create an account</Link>
                </p>
            </form>
        </div>
    );
}

export default LoginPage;
