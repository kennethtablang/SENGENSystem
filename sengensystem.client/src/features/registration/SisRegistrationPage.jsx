import { useState } from 'react';
import { Link } from 'react-router-dom';
import logo from '../../assets/SENGENlogo.png';
import TermsModal from '../auth/TermsModal';
import { registerStudent } from './api';
import { notifySuccess, notifyError } from '../shell/notify';
import {
    programOptions, studentTypeOptions, civilStatusOptions, genderOptions,
    lastSchoolLevelOptions, yearGradeOptions, termOptions, guardianRelationshipOptions
} from './options';
import './registration.css';

// Defined at module scope so their component identity is stable across renders — otherwise the
// inputs would remount on every keystroke and lose focus.
function Field({ name, label, type = 'text', required, autoComplete, placeholder, form, set, err }) {
    return (
        <div className="field">
            <label htmlFor={name}>{label}{required && ' *'}</label>
            <input
                id={name} type={type} value={form[name]} onChange={set(name)}
                autoComplete={autoComplete} placeholder={placeholder} required={required}
            />
            {err(name) && <p className="field-error">{err(name)}</p>}
        </div>
    );
}

function Select({ name, label, options, required, placeholder = 'Select…', form, set, err }) {
    return (
        <div className="field">
            <label htmlFor={name}>{label}{required && ' *'}</label>
            <select id={name} value={form[name]} onChange={set(name)} required={required}>
                <option value="" disabled>{placeholder}</option>
                {options.map(o => <option key={o.value} value={o.value}>{o.label}</option>)}
            </select>
            {err(name) && <p className="field-error">{err(name)}</p>}
        </div>
    );
}

// Official-form shell: institutional letterhead over a single document card — no marketing panel.
function FormShell({ children }) {
    return (
        <div className="sisform-page">
            <img className="sisform-watermark" src={logo} alt="" aria-hidden="true" />
            <header className="sisform-masthead">
                <div className="sisform-brand">
                    <img className="sisform-logo" src={logo} alt="" />
                    <div>
                        <p className="sisform-inst">STI College Alaminos</p>
                        <p className="sisform-tag">Office of the Registrar</p>
                    </div>
                </div>
                <div className="sisform-meta">
                    <span>Form SIS-01</span>
                    <span>AY 2026–2027</span>
                </div>
            </header>
            <main className="sisform-main">{children}</main>
            <footer className="sisform-foot">
                <span>© 2026 STI College Alaminos</span>
                <span>Personal data handled under the Data Privacy Act of 2012 (RA 10173)</span>
            </footer>
        </div>
    );
}

const initialForm = {
    studentType: 'NewStudent',
    program: '',
    lastName: '', firstName: '', middleName: '',
    dateOfBirth: '', birthplace: '', citizenship: 'Filipino',
    civilStatus: 'Single', gender: '',
    email: '', mobileNumber: '',
    addressLine: '', barangay: '', cityMunicipality: '', province: '', zipCode: '',
    lastSchoolLevel: '', schoolName: '', schoolProgram: '', schoolYear: '',
    yearGradeLastAttended: '', lastTerm: '',
    fatherName: '', fatherMobile: '', motherName: '', motherMobile: '',
    guardianRelationship: 'Mother', guardianName: '', guardianMobile: '',
    referredBy: '',
    acceptedTerms: false
};

function SisRegistrationPage() {
    const [form, setForm] = useState(initialForm);
    const [fieldErrors, setFieldErrors] = useState({});
    const [error, setError] = useState('');
    const [submitting, setSubmitting] = useState(false);
    const [termsOpen, setTermsOpen] = useState(false);
    const [result, setResult] = useState(null);

    const set = (field) => (e) => {
        const value = e.target.type === 'checkbox' ? e.target.checked : e.target.value;
        setForm(prev => ({ ...prev, [field]: value }));
    };

    const err = (name) => fieldErrors[name]?.[0];

    async function handleSubmit(e) {
        e.preventDefault();
        setError('');
        setFieldErrors({});
        setSubmitting(true);
        try {
            const data = await registerStudent(form);
            setResult(data);
            notifySuccess(`SIS submitted — your student number is ${data.studentNumber}.`);
            window.scrollTo({ top: 0, behavior: 'smooth' });
        } catch (ex) {
            notifyError(ex.message);
            setFieldErrors(ex.fieldErrors || {});
            if (!ex.fieldErrors || Object.keys(ex.fieldErrors).length === 0) {
                setError(ex.message);
            } else {
                setError('Please review the highlighted fields and try again.');
            }
        } finally {
            setSubmitting(false);
        }
    }

    if (result) {
        return (
            <FormShell>
                <article className="sisform-doc sisform-done">
                    <div className="sisform-done-check" aria-hidden="true">✓</div>
                    <h1>Registration received</h1>
                    <p className="sisform-lead">
                        Your Student Information Sheet has been submitted. A confirmation email is on its way
                        to <strong>{form.email}</strong>.
                    </p>
                    <div className="reg-number-card">
                        <span>Your student number</span>
                        <strong>{result.studentNumber}</strong>
                    </div>
                    <p className="reg-hint">
                        Keep this number safe — you'll use it for enrollment and, next term, for
                        activation. The Registrar will review your submission and requirements.
                    </p>
                    <Link className="btn btn-primary" to="/login">Back to sign in</Link>
                </article>
            </FormShell>
        );
    }

    // Shared props for every field — spread so the module-scope inputs stay controlled without
    // being recreated per render.
    const bind = { form, set, err };

    return (
        <FormShell>
            <form className="sisform-doc" onSubmit={handleSubmit} noValidate>
                <div className="sisform-doc-head">
                    <h1>Student Information Sheet</h1>
                    <p className="sisform-lead">
                        For new students and transferees — no account required. Fields marked
                        <strong> *</strong> are required. You'll be issued a student number and emailed a
                        confirmation.
                    </p>
                </div>

                {error && <div className="alert">{error}</div>}

                <fieldset className="reg-section">
                    <legend>1 · Program</legend>
                    <div className="field-row">
                        <Select name="studentType" label="Student type" options={studentTypeOptions} required {...bind} />
                        <Select name="program" label="Chosen program / track" options={programOptions} required {...bind} />
                    </div>
                </fieldset>

                <fieldset className="reg-section">
                    <legend>2 · Personal information</legend>
                    <div className="field-row">
                        <Field name="lastName" label="Last name" required autoComplete="family-name" {...bind} />
                        <Field name="firstName" label="First name" required autoComplete="given-name" {...bind} />
                    </div>
                    <div className="field-row">
                        <Field name="middleName" label="Middle name" autoComplete="additional-name" {...bind} />
                        <Field name="dateOfBirth" label="Date of birth" type="date" required {...bind} />
                    </div>
                    <div className="field-row">
                        <Field name="birthplace" label="Birthplace" required placeholder="e.g. Alaminos City, Pangasinan" {...bind} />
                        <Field name="citizenship" label="Citizenship" required {...bind} />
                    </div>
                    <div className="field-row">
                        <Select name="civilStatus" label="Civil status" options={civilStatusOptions} required {...bind} />
                        <Select name="gender" label="Gender" options={genderOptions} required {...bind} />
                    </div>
                    <div className="field-row">
                        <Field name="email" label="Email" type="email" required autoComplete="email" {...bind} />
                        <Field name="mobileNumber" label="Mobile" required placeholder="09171234567" {...bind} />
                    </div>
                </fieldset>

                <fieldset className="reg-section">
                    <legend>3 · Permanent address</legend>
                    <Field name="addressLine" label="House / lot / unit no. & street" required {...bind} />
                    <div className="field-row">
                        <Field name="barangay" label="Building / subdivision / barangay" required {...bind} />
                        <Field name="cityMunicipality" label="City / municipality" required {...bind} />
                    </div>
                    <div className="field-row">
                        <Field name="province" label="Province" required {...bind} />
                        <Field name="zipCode" label="Zip code" {...bind} />
                    </div>
                </fieldset>

                <fieldset className="reg-section">
                    <legend>4 · Last school attended</legend>
                    <div className="field-row">
                        <Select name="lastSchoolLevel" label="Level" options={lastSchoolLevelOptions} required {...bind} />
                        <Field name="schoolName" label="Name of school" required {...bind} />
                    </div>
                    <div className="field-row">
                        <Field name="schoolProgram" label="Program / track & strand" {...bind} />
                        <Field name="schoolYear" label="School year" placeholder="e.g. 2024-2025" {...bind} />
                    </div>
                    <div className="field-row">
                        <Select name="yearGradeLastAttended" label="Year / grade last attended" options={yearGradeOptions} required {...bind} />
                        <Select name="lastTerm" label="Term" options={termOptions} required {...bind} />
                    </div>
                </fieldset>

                <fieldset className="reg-section">
                    <legend>5 · Parents &amp; guardian</legend>
                    <div className="field-row">
                        <Field name="fatherName" label="Father's name" {...bind} />
                        <Field name="fatherMobile" label="Father's mobile" {...bind} />
                    </div>
                    <div className="field-row">
                        <Field name="motherName" label="Mother's name" {...bind} />
                        <Field name="motherMobile" label="Mother's mobile" {...bind} />
                    </div>
                    <div className="field-row">
                        <Select name="guardianRelationship" label="Designated guardian" options={guardianRelationshipOptions} required {...bind} />
                        <Field name="guardianName" label="Name of guardian" required {...bind} />
                    </div>
                    <div className="field-row">
                        <Field name="guardianMobile" label="Guardian's mobile" required {...bind} />
                        <Field name="referredBy" label="Referred by (optional)" {...bind} />
                    </div>
                </fieldset>

                <label className="sisform-terms">
                    <input
                        type="checkbox"
                        checked={form.acceptedTerms}
                        onChange={e => {
                            if (e.target.checked) setTermsOpen(true);
                            else setForm(prev => ({ ...prev, acceptedTerms: false }));
                        }}
                    />
                    <span>
                        I certify the information above is true and correct, and I agree to the{' '}
                        <button type="button" className="link-btn" onClick={() => setTermsOpen(true)}>
                            terms and conditions
                        </button>{' '}
                        on the processing of my personal data under RA 10173.
                    </span>
                </label>
                {err('acceptedTerms') && <p className="field-error">{err('acceptedTerms')}</p>}

                <TermsModal
                    open={termsOpen}
                    onClose={() => setTermsOpen(false)}
                    onAgree={() => { setForm(prev => ({ ...prev, acceptedTerms: true })); setTermsOpen(false); }}
                />

                <div className="sisform-actions">
                    <button className="btn btn-primary" type="submit" disabled={submitting}>
                        {submitting && <span className="spinner" aria-hidden="true" />}
                        {submitting ? 'Submitting…' : 'Submit registration'}
                    </button>
                    <p className="sisform-switch">
                        Returning student? <Link to="/term-activation">Activate your term</Link>
                        {' · '}
                        <Link to="/login">Sign in</Link>
                    </p>
                </div>
            </form>
        </FormShell>
    );
}

export default SisRegistrationPage;
