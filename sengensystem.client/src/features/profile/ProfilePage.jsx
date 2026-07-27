import { useEffect, useState } from 'react';
import { useAuth } from '../auth/useAuth';
import PasswordInput from '../auth/PasswordInput';
import { changePassword, updateProfile, startTwoFactor, enableTwoFactor, disableTwoFactor } from './api';
import { getMyLink } from '../documents/api';
import ChangeEmailModal from './ChangeEmailModal';
import { notifySuccess, notifyError } from '../shell/notify';
import './profile.css';

/* Two-factor authentication (opt-in email one-time code). Enabling is a two-step email-code
   confirmation; disabling re-checks the account password. */
function TwoFactorCard({ user, updateUser }) {
    const [stage, setStage] = useState('idle'); // 'idle' | 'confirm' (enter code) | 'disable' (enter password)
    const [code, setCode] = useState('');
    const [password, setPassword] = useState('');
    const [busy, setBusy] = useState(false);
    const [alert, setAlert] = useState(null);
    const [errors, setErrors] = useState({});

    const enabled = user.twoFactorEnabled;

    async function start() {
        setBusy(true); setAlert(null); setErrors({});
        try {
            const res = await startTwoFactor();
            setStage('confirm');
            setAlert({ kind: 'success', text: res.message });
        } catch (ex) {
            notifyError(ex.message);
            setAlert({ kind: 'error', text: ex.message });
        } finally { setBusy(false); }
    }

    async function confirmEnable(e) {
        e.preventDefault();
        setBusy(true); setErrors({}); setAlert(null);
        try {
            await enableTwoFactor(code.trim());
            updateUser({ ...user, twoFactorEnabled: true });
            setStage('idle'); setCode('');
            notifySuccess('Two-factor authentication is on.');
        } catch (ex) {
            setErrors(ex.fieldErrors || {});
            if (!ex.fieldErrors || Object.keys(ex.fieldErrors).length === 0) notifyError(ex.message);
        } finally { setBusy(false); }
    }

    async function confirmDisable(e) {
        e.preventDefault();
        setBusy(true); setErrors({}); setAlert(null);
        try {
            await disableTwoFactor(password);
            updateUser({ ...user, twoFactorEnabled: false });
            setStage('idle'); setPassword('');
            notifySuccess('Two-factor authentication is off.');
        } catch (ex) {
            setErrors(ex.fieldErrors || {});
            if (!ex.fieldErrors || Object.keys(ex.fieldErrors).length === 0) notifyError(ex.message);
        } finally { setBusy(false); }
    }

    function cancel() {
        setStage('idle'); setCode(''); setPassword(''); setErrors({}); setAlert(null);
    }

    return (
        <section className="card profile-card">
            <h3>Two-factor authentication</h3>
            <p className="profile-card-sub">
                Add a second step at sign-in: a 6-digit code emailed to <strong>{user.email}</strong>.
                Keeps your account safe even if your password is guessed.
            </p>

            <p style={{ margin: '0 0 0.9rem' }}>
                <span className={enabled ? 'chip chip-blue' : 'chip chip-muted'}>
                    {enabled ? 'On' : 'Off'}
                </span>
            </p>

            {alert && (
                <div className={alert.kind === 'success' ? 'alert alert-success' : 'alert'}>{alert.text}</div>
            )}

            {!enabled && stage === 'idle' && (
                <button type="button" className="btn btn-primary" disabled={busy} onClick={start}>
                    {busy && <span className="spinner" aria-hidden="true" />}
                    {busy ? 'Sending code…' : 'Turn on'}
                </button>
            )}

            {!enabled && stage === 'confirm' && (
                <form onSubmit={confirmEnable} noValidate>
                    <div className="field">
                        <label htmlFor="tfa-code">Enter the emailed code</label>
                        <input
                            id="tfa-code" type="text" inputMode="numeric" autoComplete="one-time-code"
                            maxLength={6} placeholder="123456" value={code}
                            onChange={e => setCode(e.target.value.replace(/\D/g, ''))} autoFocus
                            style={{ letterSpacing: '0.3em' }}
                        />
                        {errors.code && <p className="field-error">{errors.code[0]}</p>}
                    </div>
                    <div style={{ display: 'flex', gap: '0.5rem' }}>
                        <button type="submit" className="btn btn-primary" disabled={busy || code.length < 6}>
                            {busy && <span className="spinner" aria-hidden="true" />}
                            {busy ? 'Verifying…' : 'Confirm and turn on'}
                        </button>
                        <button type="button" className="btn btn-ghost" onClick={cancel}>Cancel</button>
                        <button type="button" className="btn btn-ghost" onClick={start} disabled={busy}>Resend</button>
                    </div>
                </form>
            )}

            {enabled && stage !== 'disable' && (
                <button type="button" className="btn btn-ghost" onClick={() => setStage('disable')}>
                    Turn off
                </button>
            )}

            {enabled && stage === 'disable' && (
                <form onSubmit={confirmDisable} noValidate>
                    <div className="field">
                        <label htmlFor="tfa-pw">Confirm your password to turn it off</label>
                        <PasswordInput id="tfa-pw" autoComplete="current-password" value={password}
                            onChange={e => setPassword(e.target.value)} />
                        {errors.password && <p className="field-error">{errors.password[0]}</p>}
                    </div>
                    <div style={{ display: 'flex', gap: '0.5rem' }}>
                        <button type="submit" className="btn btn-danger" disabled={busy || !password}>
                            {busy && <span className="spinner" aria-hidden="true" />}
                            {busy ? 'Turning off…' : 'Turn off 2FA'}
                        </button>
                        <button type="button" className="btn btn-ghost" onClick={cancel}>Cancel</button>
                    </div>
                </form>
            )}
        </section>
    );
}

/* A student's own record card: the student number they are asked for everywhere off this system,
   plus the program and year level their enlistment is scoped to. Read-only — every value here is
   assigned by the Admission Office or Registrar, and this is where a student comes to look it up
   rather than dig through an email. Students only; other roles have no registration record. */
function StudentRecordCard() {
    const [state, setState] = useState(null);
    const [error, setError] = useState('');

    useEffect(() => {
        let active = true;
        getMyLink()
            .then(data => { if (active) setState(data); })
            .catch(err => { if (active) setError(err.message); });
        return () => { active = false; };
    }, []);

    if (error) {
        return (
            <section className="card profile-card">
                <h3>Student record</h3>
                <div className="alert">{error}</div>
            </section>
        );
    }

    if (!state) {
        return (
            <section className="card profile-card">
                <h3>Student record</h3>
                <p className="profile-card-sub">Loading…</p>
            </section>
        );
    }

    if (!state.linked) {
        return (
            <section className="card profile-card">
                <h3>Student record</h3>
                <p className="profile-card-sub">
                    Your account is not linked to a student record yet. Claim it under Document
                    requirements — your student number appears here once it is.
                </p>
            </section>
        );
    }

    const r = state.registration;

    return (
        <section className="card profile-card">
            <h3>Student record</h3>
            <p className="profile-card-sub">
                Issued by the school — you cannot change these here. Quote your student number when
                dealing with the Registrar or the Admission Office.
            </p>

            <dl className="profile-facts">
                <div>
                    <dt>Student number</dt>
                    {/* The official number from the student-records system, assigned by the Admission
                        Officer. Until that happens the registration number is all the student has —
                        show it as what it is rather than presenting it as their student number. */}
                    <dd className="profile-fact-strong">
                        {r.officialStudentNumber ?? 'Not yet assigned'}
                    </dd>
                </div>
                <div>
                    <dt>Registration number</dt>
                    <dd>{r.studentNumber}</dd>
                </div>
                <div>
                    <dt>Program</dt>
                    <dd>{r.program}</dd>
                </div>
                <div>
                    <dt>Year level</dt>
                    <dd>{r.yearLevelLabel}</dd>
                </div>
            </dl>

            {!r.officialStudentNumber && (
                <p className="profile-card-note">
                    Your official student number is assigned by the Admission Office once you are
                    enrolled in the student-records system. Use your registration number until then.
                </p>
            )}
        </section>
    );
}

const roleLabels = {
    Student: 'Student',
    FacultyMember: 'Faculty Member',
    AdmissionOfficer: 'Admission Officer',
    Registrar: 'Registrar',
    AcademicHead: 'Academic Head',
    SchoolAdmin: 'School Admin',
    SuperAdmin: 'Super Admin'
};

function ProfilePage() {
    const { user, updateUser } = useAuth();

    const [info, setInfo] = useState({
        firstName: user.firstName,
        lastName: user.lastName,
        email: user.email
    });
    const [infoErrors, setInfoErrors] = useState({});
    const [infoAlert, setInfoAlert] = useState(null); // { kind: 'success'|'error', text }
    const [savingInfo, setSavingInfo] = useState(false);

    const [pw, setPw] = useState({ current: '', next: '', confirm: '' });
    const [pwErrors, setPwErrors] = useState({});
    const [pwAlert, setPwAlert] = useState(null);
    const [savingPw, setSavingPw] = useState(false);
    const [emailModalOpen, setEmailModalOpen] = useState(false);

    const initials = `${user.firstName[0] ?? ''}${user.lastName[0] ?? ''}`.toUpperCase();

    const setInfoField = field => e => setInfo(prev => ({ ...prev, [field]: e.target.value }));
    const setPwField = field => e => setPw(prev => ({ ...prev, [field]: e.target.value }));

    const infoError = name => infoErrors[name]?.[0];
    const pwError = name => pwErrors[name]?.[0];

    async function saveInfo(e) {
        e.preventDefault();
        setInfoErrors({});
        setInfoAlert(null);
        setSavingInfo(true);
        try {
            const { user: updated } = await updateProfile({ ...info, email: user.email });
            updateUser(updated);
            setInfo({ firstName: updated.firstName, lastName: updated.lastName, email: updated.email });
            setInfoAlert({ kind: 'success', text: 'Profile updated.' });
            notifySuccess('Profile updated.');
        } catch (err) {
            notifyError(err.message);
            setInfoErrors(err.fieldErrors || {});
            if (!err.fieldErrors || Object.keys(err.fieldErrors).length === 0) {
                setInfoAlert({ kind: 'error', text: err.message });
            }
        } finally {
            setSavingInfo(false);
        }
    }

    async function savePassword(e) {
        e.preventDefault();
        setPwErrors({});
        setPwAlert(null);

        if (pw.next !== pw.confirm) {
            setPwErrors({ confirm: ['Passwords do not match.'] });
            return;
        }

        setSavingPw(true);
        try {
            await changePassword({ currentPassword: pw.current, newPassword: pw.next });
            setPw({ current: '', next: '', confirm: '' });
            setPwAlert({ kind: 'success', text: 'Password updated.' });
            notifySuccess('Password updated.');
        } catch (err) {
            notifyError(err.message);
            setPwErrors(err.fieldErrors || {});
            if (!err.fieldErrors || Object.keys(err.fieldErrors).length === 0) {
                setPwAlert({ kind: 'error', text: err.message });
            }
        } finally {
            setSavingPw(false);
        }
    }

    return (
        <div className="profile-page">
            <header className="profile-head">
                <span className="avatar profile-avatar">{initials}</span>
                <div>
                    <h2>{user.firstName} {user.lastName}</h2>
                    <p>
                        <span className="chip chip-blue">{roleLabels[user.role] ?? user.role}</span>
                        <span className="profile-email">{user.email}</span>
                    </p>
                </div>
            </header>

            <div className="profile-grid">
                {user.role === 'Student' && <StudentRecordCard />}

                <form className="card profile-card" onSubmit={saveInfo} noValidate>
                    <h3>Personal information</h3>
                    <p className="profile-card-sub">
                        Your name is stored with proper capitalization and appears on
                        enrollment records and schedules.
                    </p>

                    {infoAlert && (
                        <div className={infoAlert.kind === 'success' ? 'alert alert-success' : 'alert'}>
                            {infoAlert.text}
                        </div>
                    )}

                    <div className="field-row">
                        <div className="field">
                            <label htmlFor="pf-first">First name</label>
                            <input id="pf-first" type="text" value={info.firstName} onChange={setInfoField('firstName')} required />
                            {infoError('firstName') && <p className="field-error">{infoError('firstName')}</p>}
                        </div>
                        <div className="field">
                            <label htmlFor="pf-last">Last name</label>
                            <input id="pf-last" type="text" value={info.lastName} onChange={setInfoField('lastName')} required />
                            {infoError('lastName') && <p className="field-error">{infoError('lastName')}</p>}
                        </div>
                    </div>

                    <div className="field">
                        <label htmlFor="pf-email">Email</label>
                        <div style={{ display: 'flex', gap: '0.5rem', alignItems: 'center' }}>
                            <input id="pf-email" type="email" value={user.email} disabled style={{ flex: 1 }} />
                            <button type="button" className="btn btn-ghost" onClick={() => setEmailModalOpen(true)}>
                                Change email
                            </button>
                        </div>
                        <p style={{ fontSize: '0.76rem', color: 'var(--text-3)', marginTop: '0.3rem' }}>
                            Changing your email requires a confirmation link sent to the new address.
                        </p>
                        {infoError('email') && <p className="field-error">{infoError('email')}</p>}
                    </div>

                    <button className="btn btn-primary" type="submit" disabled={savingInfo}>
                        {savingInfo && <span className="spinner" aria-hidden="true" />}
                        {savingInfo ? 'Saving…' : 'Save changes'}
                    </button>
                </form>

                <form className="card profile-card" onSubmit={savePassword} noValidate>
                    <h3>Change password</h3>
                    <p className="profile-card-sub">
                        At least 8 characters with both letters and digits.
                    </p>

                    {pwAlert && (
                        <div className={pwAlert.kind === 'success' ? 'alert alert-success' : 'alert'}>
                            {pwAlert.text}
                        </div>
                    )}

                    <div className="field">
                        <label htmlFor="pf-current">Current password</label>
                        <PasswordInput id="pf-current" autoComplete="current-password" value={pw.current} onChange={setPwField('current')} />
                        {pwError('currentPassword') && <p className="field-error">{pwError('currentPassword')}</p>}
                    </div>

                    <div className="field">
                        <label htmlFor="pf-next">New password</label>
                        <PasswordInput id="pf-next" autoComplete="new-password" value={pw.next} onChange={setPwField('next')} />
                        {pwError('newPassword') && <p className="field-error">{pwError('newPassword')}</p>}
                    </div>

                    <div className="field">
                        <label htmlFor="pf-confirm">Confirm new password</label>
                        <PasswordInput id="pf-confirm" autoComplete="new-password" value={pw.confirm} onChange={setPwField('confirm')} />
                        {pwError('confirm') && <p className="field-error">{pwError('confirm')}</p>}
                    </div>

                    <button className="btn btn-primary" type="submit" disabled={savingPw}>
                        {savingPw && <span className="spinner" aria-hidden="true" />}
                        {savingPw ? 'Updating…' : 'Update password'}
                    </button>
                </form>

                <TwoFactorCard user={user} updateUser={updateUser} />
            </div>

            {emailModalOpen && (
                <ChangeEmailModal currentEmail={user.email} onClose={() => setEmailModalOpen(false)} />
            )}
        </div>
    );
}

export default ProfilePage;
