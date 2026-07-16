import { useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { registerAccount } from './api';
import AuthLayout from './AuthLayout';
import PasswordInput from './PasswordInput';
import TermsModal from './TermsModal';

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
    const [termsOpen, setTermsOpen] = useState(false);

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
        <AuthLayout>
            <form className="auth-card" onSubmit={handleSubmit} noValidate>
                <h1>Create your account</h1>
                <p className="auth-subtitle">
                    One account for documents, registration, and enlistment.
                </p>

                {error && <div className="alert">{error}</div>}

                <div className="field-row">
                    <div className="field">
                        <label htmlFor="firstName">First name</label>
                        <input id="firstName" type="text" autoComplete="given-name" value={form.firstName} onChange={set('firstName')} required />
                        {fieldError('firstName') && <p className="field-error">{fieldError('firstName')}</p>}
                    </div>
                    <div className="field">
                        <label htmlFor="lastName">Last name</label>
                        <input id="lastName" type="text" autoComplete="family-name" value={form.lastName} onChange={set('lastName')} required />
                        {fieldError('lastName') && <p className="field-error">{fieldError('lastName')}</p>}
                    </div>
                </div>

                <div className="field">
                    <label htmlFor="email">Email</label>
                    <input id="email" type="email" autoComplete="email" value={form.email} onChange={set('email')} required />
                    {fieldError('email') && <p className="field-error">{fieldError('email')}</p>}
                </div>

                <div className="field">
                    <label htmlFor="password">Password</label>
                    <PasswordInput id="password" autoComplete="new-password" value={form.password} onChange={set('password')} />
                    {fieldError('password') && <p className="field-error">{fieldError('password')}</p>}
                </div>

                <div className="field">
                    <label htmlFor="confirmPassword">Confirm password</label>
                    <PasswordInput id="confirmPassword" autoComplete="new-password" value={form.confirmPassword} onChange={set('confirmPassword')} />
                    {fieldError('confirmPassword') && <p className="field-error">{fieldError('confirmPassword')}</p>}
                </div>

                <label className="auth-terms">
                    <input
                        type="checkbox"
                        checked={form.acceptedTerms}
                        onChange={e => {
                            if (e.target.checked) {
                                // Agreement only happens inside the modal, after reading.
                                setTermsOpen(true);
                            } else {
                                setForm(prev => ({ ...prev, acceptedTerms: false }));
                            }
                        }}
                    />
                    <span>
                        I have read and agree to the{' '}
                        <button type="button" className="link-btn" onClick={() => setTermsOpen(true)}>
                            terms and conditions
                        </button>{' '}
                        on the collection and processing of my personal data under the
                        Data Privacy Act of 2012 (RA 10173).
                    </span>
                </label>
                {fieldError('acceptedTerms') && <p className="field-error">{fieldError('acceptedTerms')}</p>}

                <TermsModal
                    open={termsOpen}
                    onClose={() => setTermsOpen(false)}
                    onAgree={() => {
                        setForm(prev => ({ ...prev, acceptedTerms: true }));
                        setTermsOpen(false);
                    }}
                />

                <button className="btn btn-primary btn-block" type="submit" disabled={submitting}>
                    {submitting && <span className="spinner" aria-hidden="true" />}
                    {submitting ? 'Creating account…' : 'Create account'}
                </button>

                <p className="auth-switch">
                    Already registered? <Link to="/login">Sign in</Link>
                </p>
            </form>
        </AuthLayout>
    );
}

export default RegisterPage;
