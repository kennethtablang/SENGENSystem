import { useState } from 'react';
import { Link } from 'react-router-dom';
import { forgotPassword } from './api';
import AuthLayout from './AuthLayout';
import { notifyError } from '../shell/notify';

function ForgotPasswordPage() {
    const [email, setEmail] = useState('');
    const [sent, setSent] = useState(null); // server's neutral confirmation text
    const [error, setError] = useState('');
    const [submitting, setSubmitting] = useState(false);

    async function handleSubmit(e) {
        e.preventDefault();
        setError('');
        setSubmitting(true);
        try {
            const { message } = await forgotPassword(email.trim());
            setSent(message);
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
                <h1>Forgot password</h1>
                <p className="auth-subtitle">
                    Enter your account email and we&rsquo;ll send you a link to choose a new password.
                </p>

                {sent && <div className="alert alert-success">{sent}</div>}
                {error && <div className="alert">{error}</div>}

                <div className="field">
                    <label htmlFor="fp-email">Email</label>
                    <input
                        id="fp-email"
                        type="email"
                        autoComplete="email"
                        value={email}
                        onChange={e => setEmail(e.target.value)}
                        required
                    />
                </div>

                <button className="btn btn-primary btn-block" type="submit" disabled={submitting || !email.trim()}>
                    {submitting && <span className="spinner" aria-hidden="true" />}
                    {submitting ? 'Sending…' : sent ? 'Send again' : 'Send reset link'}
                </button>

                <p className="auth-switch">
                    Remembered it? <Link to="/login">Back to sign in</Link>
                </p>
            </form>
        </AuthLayout>
    );
}

export default ForgotPasswordPage;
