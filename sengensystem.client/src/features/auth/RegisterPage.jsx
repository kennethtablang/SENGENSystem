import { useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { registerAccount } from './api';
import './auth.css';

const initialForm = {
    firstName: '',
    lastName: '',
    email: '',
    password: '',
    confirmPassword: '',
    acceptedTerms: false
};

function RegisterPage() {
    const navigate = useNavigate();
    const [form, setForm] = useState(initialForm);
    const [fieldErrors, setFieldErrors] = useState({});
    const [error, setError] = useState('');
    const [submitting, setSubmitting] = useState(false);

    const set = (field) => (e) => {
        const value = e.target.type === 'checkbox' ? e.target.checked : e.target.value;
        setForm(prev => ({ ...prev, [field]: value }));
    };

    async function handleSubmit(e) {
        e.preventDefault();
        setError('');
        setFieldErrors({});

        if (form.password !== form.confirmPassword) {
            setFieldErrors({ confirmPassword: ['Passwords do not match.'] });
            return;
        }

        setSubmitting(true);
        try {
            await registerAccount({
                firstName: form.firstName,
                lastName: form.lastName,
                email: form.email,
                password: form.password,
                acceptedTerms: form.acceptedTerms
            });
            navigate('/login', { state: { registered: true } });
        } catch (err) {
            setFieldErrors(err.fieldErrors || {});
            if (!err.fieldErrors || Object.keys(err.fieldErrors).length === 0) {
                setError(err.message);
            }
        } finally {
            setSubmitting(false);
        }
    }

    const fieldError = (name) => fieldErrors[name]?.[0];

    return (
        <div className="auth-page">
            <form className="auth-card" onSubmit={handleSubmit} noValidate>
                <h1>Create your account</h1>
                <p className="auth-subtitle">
                    SEN-GEN Student Enrollment &amp; Class Scheduling — STI Alaminos
                </p>

                {error && <div className="auth-alert">{error}</div>}

                <div className="auth-field-row">
                    <div className="auth-field">
                        <label htmlFor="firstName">First name</label>
                        <input id="firstName" type="text" value={form.firstName} onChange={set('firstName')} required />
                        {fieldError('firstName') && <p className="auth-error">{fieldError('firstName')}</p>}
                    </div>
                    <div className="auth-field">
                        <label htmlFor="lastName">Last name</label>
                        <input id="lastName" type="text" value={form.lastName} onChange={set('lastName')} required />
                        {fieldError('lastName') && <p className="auth-error">{fieldError('lastName')}</p>}
                    </div>
                </div>

                <div className="auth-field">
                    <label htmlFor="email">Email address</label>
                    <input id="email" type="email" value={form.email} onChange={set('email')} required />
                    {fieldError('email') && <p className="auth-error">{fieldError('email')}</p>}
                </div>

                <div className="auth-field">
                    <label htmlFor="password">Password</label>
                    <input id="password" type="password" value={form.password} onChange={set('password')} required />
                    {fieldError('password') && <p className="auth-error">{fieldError('password')}</p>}
                </div>

                <div className="auth-field">
                    <label htmlFor="confirmPassword">Confirm password</label>
                    <input id="confirmPassword" type="password" value={form.confirmPassword} onChange={set('confirmPassword')} required />
                    {fieldError('confirmPassword') && <p className="auth-error">{fieldError('confirmPassword')}</p>}
                </div>

                <label className="auth-terms">
                    <input type="checkbox" checked={form.acceptedTerms} onChange={set('acceptedTerms')} />
                    <span>
                        I have read and agree to the terms and conditions on the collection and
                        processing of my personal data in accordance with the Data Privacy Act of 2012 (RA 10173).
                    </span>
                </label>
                {fieldError('acceptedTerms') && <p className="auth-error">{fieldError('acceptedTerms')}</p>}

                <button className="auth-submit" type="submit" disabled={submitting}>
                    {submitting ? 'Creating account…' : 'Register'}
                </button>

                <p className="auth-switch">
                    Already have an account? <Link to="/login">Sign in</Link>
                </p>
            </form>
        </div>
    );
}

export default RegisterPage;
