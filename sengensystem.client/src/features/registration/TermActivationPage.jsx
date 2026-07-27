import { useState } from 'react';
import { Link } from 'react-router-dom';
import AuthLayout from '../auth/AuthLayout';
import { lookupTermActivation, requestTermActivation } from './api';
import { notifySuccess, notifyError } from '../shell/notify';
import './registration.css';

/* Returning-student term activation, in two deliberate steps.

   Step 1 (lookup) identifies the student. Step 2 (confirm) shows them the two things activation
   actually decides — the year level they are coming back into and the term they are activating
   for — and asks them to agree before anything is filed. Previously this was a single submit, so
   a student who had been promoted (or held back) by mistake found out from the confirmation email.
   Checking is cheap here and expensive later. The year level they confirm is filed with the
   request; the Admission Officer still has the final say when they validate it. */

const yearOptions = (min, max) =>
    Array.from({ length: max - min + 1 }, (_, i) => min + i);

const yearLabel = (n) => (['1st year', '2nd year', '3rd year', '4th year'][n - 1] ?? `Year ${n}`);

function TermActivationPage() {
    const [form, setForm] = useState({ studentNumber: '', lastName: '' });
    const [fieldErrors, setFieldErrors] = useState({});
    const [error, setError] = useState('');
    const [busy, setBusy] = useState(false);
    const [found, setFound] = useState(null);    // lookup payload — drives the confirm step
    const [yearLevel, setYearLevel] = useState(null);
    const [agreed, setAgreed] = useState(false);
    const [result, setResult] = useState(null);

    const set = (field) => (e) => setForm(prev => ({ ...prev, [field]: e.target.value }));
    const err = (name) => fieldErrors[name]?.[0];

    function fail(ex) {
        notifyError(ex.message);
        setFieldErrors(ex.fieldErrors || {});
        if (!ex.fieldErrors || Object.keys(ex.fieldErrors).length === 0) {
            setError(ex.message);
        }
    }

    async function handleLookup(e) {
        e.preventDefault();
        setError('');
        setFieldErrors({});
        setBusy(true);
        try {
            const data = await lookupTermActivation(form);
            setFound(data);
            // Default to what the school derives, so the student confirms rather than guesses —
            // and changing it is a deliberate act they had to make.
            setYearLevel(data.proposedYearLevel);
            setAgreed(false);
        } catch (ex) {
            fail(ex);
        } finally {
            setBusy(false);
        }
    }

    async function handleConfirm(e) {
        e.preventDefault();
        setError('');
        setFieldErrors({});
        setBusy(true);
        try {
            const data = await requestTermActivation({
                ...form,
                semesterId: found.semesterId,
                yearLevel,
                confirmed: agreed
            });
            setResult(data);
            notifySuccess('Term activation requested — the Admission Office will validate it.');
        } catch (ex) {
            fail(ex);
        } finally {
            setBusy(false);
        }
    }

    function startOver() {
        setFound(null);
        setYearLevel(null);
        setAgreed(false);
        setError('');
        setFieldErrors({});
    }

    // ---- Step 3: filed ----
    if (result) {
        return (
            <AuthLayout>
                <div className="auth-card reg-done">
                    <div className="reg-done-check" aria-hidden="true">✓</div>
                    <h1>Activation requested</h1>
                    <p className="auth-subtitle">
                        Your request to activate for <strong>{result.semesterName}</strong> as{' '}
                        <strong>{result.declaredYearLevelLabel}</strong> has been received and is now
                        pending review by our Admission Officer.
                    </p>
                    <div className="reg-number-card">
                        <span>Student number</span>
                        <strong>{result.studentNumber}</strong>
                    </div>
                    <p className="reg-hint">
                        A receipt has been emailed to you as proof of this request. Once it's validated,
                        you'll receive a confirmation email. No need to re-submit your SIS.
                    </p>
                    <Link className="btn btn-primary btn-block" to="/login">Back to sign in</Link>
                </div>
            </AuthLayout>
        );
    }

    // ---- Step 2: check your year level and term, then finalize ----
    if (found) {
        const changed = yearLevel !== found.proposedYearLevel;
        return (
            <AuthLayout>
                <form className="auth-card" onSubmit={handleConfirm} noValidate>
                    <h1>Check your details</h1>
                    <p className="auth-subtitle">
                        Confirm the year level and term you are coming back into. Nothing is filed until
                        you finalize below.
                    </p>

                    {error && <div className="alert">{error}</div>}

                    {found.alreadyFiled && (
                        <div className="alert">
                            You already have a {found.existingStatus?.toLowerCase()} term activation on file
                            for {found.semesterName}. There is no need to file another.
                        </div>
                    )}

                    <div className="reg-confirm-card">
                        <div className="reg-confirm-row">
                            <span>Student</span>
                            <strong>{found.fullName}</strong>
                        </div>
                        <div className="reg-confirm-row">
                            <span>Student number</span>
                            <strong className="reg-mono">{found.studentNumber}</strong>
                        </div>
                        <div className="reg-confirm-row">
                            <span>Program</span>
                            <strong>{found.program}</strong>
                        </div>
                        <div className="reg-confirm-row">
                            <span>Activating for</span>
                            <strong>{found.semesterName}</strong>
                        </div>
                        <div className="reg-confirm-row">
                            <span>Term</span>
                            <strong>{found.termLabel}</strong>
                        </div>
                        <div className="reg-confirm-row">
                            <span>Currently on record</span>
                            <strong>{found.currentYearLevelLabel}</strong>
                        </div>
                    </div>

                    <div className="field">
                        <label htmlFor="yearLevel">Year level you are enrolling into</label>
                        <select
                            id="yearLevel" value={yearLevel ?? found.proposedYearLevel}
                            onChange={e => setYearLevel(Number(e.target.value))}
                        >
                            {yearOptions(found.minYearLevel, found.maxYearLevel).map(n => (
                                <option key={n} value={n}>{yearLabel(n)}</option>
                            ))}
                        </select>
                        {/* Say where the default came from. A student moving into a new school year is
                            promoted a year; one activating for the second semester of the year they
                            are already in stays put. */}
                        <p className="field-hint">
                            {found.isNewSchoolYear
                                ? `A new school year, so you move up from ${found.currentYearLevelLabel}. `
                                : 'Still within the same school year, so your year level does not change. '}
                            If this is wrong, choose the right one — the Admission Office checks it against
                            your records before approving.
                        </p>
                        {changed && (
                            <p className="field-hint reg-confirm-changed">
                                You changed this from {found.proposedYearLevelLabel}. Expect the Admission
                                Office to verify it against your records.
                            </p>
                        )}
                        {err('yearLevel') && <p className="field-error">{err('yearLevel')}</p>}
                    </div>

                    <label className="reg-confirm-check">
                        <input
                            type="checkbox" checked={agreed}
                            onChange={e => setAgreed(e.target.checked)}
                        />
                        <span>
                            I confirm I am activating for <strong>{found.semesterName}</strong> as{' '}
                            <strong>{yearLabel(yearLevel ?? found.proposedYearLevel)}</strong>.
                        </span>
                    </label>
                    {err('confirmed') && <p className="field-error">{err('confirmed')}</p>}
                    {err('semesterId') && <p className="field-error">{err('semesterId')}</p>}

                    <button
                        className="btn btn-primary btn-block" type="submit"
                        disabled={busy || !agreed || found.alreadyFiled}
                    >
                        {busy && <span className="spinner" aria-hidden="true" />}
                        {busy ? 'Finalizing…' : 'Finalize term activation'}
                    </button>

                    <button className="btn btn-ghost btn-block" type="button" onClick={startOver}>
                        Back
                    </button>
                </form>
            </AuthLayout>
        );
    }

    // ---- Step 1: who are you ----
    return (
        <AuthLayout>
            <form className="auth-card" onSubmit={handleLookup} noValidate>
                <h1>Term activation</h1>
                <p className="auth-subtitle">
                    Returning students — activate your enrollment for the new term. Enter your student
                    number and your last name; you'll check your year level and term on the next step.
                </p>

                {error && <div className="alert">{error}</div>}

                <div className="field">
                    <label htmlFor="studentNumber">Student number</label>
                    <input
                        id="studentNumber" type="text" value={form.studentNumber}
                        onChange={set('studentNumber')} placeholder="e.g. 02000123456" required
                    />
                    {/* A returning student has carried their student number for years; the internal
                        registration number is an artifact of the term they first enrolled. It still
                        works here, for anyone never issued a student number. */}
                    <p className="field-hint">
                        The number on your student ID. If you have not been issued one yet, use the
                        registration number from your original registration.
                    </p>
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

                <button className="btn btn-primary btn-block" type="submit" disabled={busy}>
                    {busy && <span className="spinner" aria-hidden="true" />}
                    {busy ? 'Looking up…' : 'Continue'}
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
