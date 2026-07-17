import { useState } from 'react';
import { useAuth } from '../auth/useAuth';
import PasswordInput from '../auth/PasswordInput';
import { changePassword, updateProfile } from './api';
import ChangeEmailModal from './ChangeEmailModal';
import { notifySuccess, notifyError } from '../shell/notify';
import './profile.css';

const roleLabels = {
    Student: 'Student',
    FacultyMember: 'Faculty Member',
    AdmissionOfficer: 'Admission Officer',
    Registrar: 'Registrar',
    AcademicHead: 'Academic Head',
    SchoolAdmin: 'School Admin'
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
            </div>

            {emailModalOpen && (
                <ChangeEmailModal currentEmail={user.email} onClose={() => setEmailModalOpen(false)} />
            )}
        </div>
    );
}

export default ProfilePage;
