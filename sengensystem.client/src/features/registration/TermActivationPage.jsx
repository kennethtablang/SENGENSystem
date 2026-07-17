import { useState } from 'react';
import { Link } from 'react-router-dom';
import AuthLayout from '../auth/AuthLayout';
import { requestTermActivation } from './api';
import { notifySuccess, notifyError } from '../shell/notify';
import './registration.css';

function TermActivationPage() {
    const [form, setForm] = useState({ studentNumber: '', lastName: '' });
    const [fieldErrors, setFieldErrors] = useState({});
    const [error, setError] = useState('');
    const [submitting, setSubmitting] = useState(false);
    const [result, setResult] = useState(null);

    const set = (field) => (e) => setForm(prev => ({ ...prev, [field]: e.target.value }));
    const err = (name) => fieldErrors[name]?.[0];

    async function handleSubmit(e) {
        e.preventDefault();
        setError('');
        setFieldErrors({});
        setSubmitting(true);
        try {
            const data = await requestTermActivation(form);
            setResult(data);
            notifySuccess('Term activation requested — the Admission Office will validate it.');
        } catch (ex) {
            notifyError(ex.message);
            setFieldErrors(ex.fieldErrors || {});
            if (!ex.fieldErrors || Object.keys(ex.fieldErrors).length === 0) {
                setError(ex.message);
            }
        } finally {
            setSubmitting(false);
        }
    }

    if (result) {
        return (
            <AuthLayout>
                <div className="auth-card reg-done">
                    <div className="reg-done-check" aria-hidden="true">✓</div>
                    <h1>Activation requested</h1>
                    <p className="auth-subtitle">
                        Your request to activate for <strong>{result.semesterName}</strong> has been received and
                        is now pending review by our Admission Officer.
                    </p>
                    <div className="reg-number-card">
                        <span>Student number</span>
                        <strong>{result.studentNumber}</strong>
                    </div>
                    <p className="reg-hint">
                        Once validated, you'll receive a confirmation email. No need to re-submit your SIS.
                    </p>
                    <Link className="btn btn-primary btn-block" to="/login">Back to sign in</Link>
                </div>
            </AuthLayout>
        );
    }

    return (
        <AuthLayout>
            <form className="auth-card" onSubmit={handleSubmit} noValidate>
                <h1>Term activation</h1>
                <p className="auth-subtitle">
                    Returning students — activate your enrollment for the new term. Enter the student number
                    from your original registration and your last name.
                </p>

                {error && <div className="alert">{error}</div>}

                <div className="field">
                    <label htmlFor="studentNumber">Student number</label>
                    <input
                        id="studentNumber" type="text" value={form.studentNumber}
                        onChange={set('studentNumber')} placeholder="e.g. 2025-000001" required
                    />
                    {err('studentNumber') && <p className="field-error">{err('studentNumber')}</p>}
                </div>

                <div className="field">
                    <label htmlFor="lastName">Last name</label>
                    <input
                        id="lastName" type="text" value={form.lastName}
                        onChange={set('lastName')} autoComplete="family-name" required
                    />
                    {err('lastName') && <p className="field-error">{err('lastName')}</p>}
                </div>

                <button className="btn btn-primary btn-block" type="submit" disabled={submitting}>
                    {submitting && <span className="spinner" aria-hidden="true" />}
                    {submitting ? 'Submitting…' : 'Request activation'}
                </button>

                <p className="auth-switch">
                    New student? <Link to="/register-sis">Register here</Link>
                    {' · '}
                    <Link to="/login">Sign in</Link>
                </p>
            </form>
        </AuthLayout>
    );
}

export default TermActivationPage;
