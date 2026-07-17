import { useEffect, useState } from 'react';
import { createPortal } from 'react-dom';
import { useAuth } from '../auth/useAuth';
import { listUsers, createUser, updateUser, setUserActive, resetUserPassword } from './api';
import { notifySuccess, notifyError } from '../shell/notify';
import './users.css';

const roleOptions = [
    { value: 'Student', label: 'Student' },
    { value: 'FacultyMember', label: 'Faculty Member' },
    { value: 'AdmissionOfficer', label: 'Admission Officer' },
    { value: 'Registrar', label: 'Registrar' },
    { value: 'AcademicHead', label: 'Academic Head' },
    { value: 'SchoolAdmin', label: 'School Admin' }
];
const roleLabel = (v) => roleOptions.find(r => r.value === v)?.label ?? v;

function formatPHT(iso) {
    if (!iso) return '—';
    const d = new Date(iso);
    if (Number.isNaN(d.getTime())) return iso;
    return d.toLocaleString('en-PH', {
        timeZone: 'Asia/Manila',
        year: 'numeric', month: 'short', day: '2-digit',
        hour: '2-digit', minute: '2-digit', hour12: true
    });
}

const blankForm = { firstName: '', lastName: '', email: '', role: 'Student', password: '' };

function UserModal({ mode, user, currentUserId, onClose, onChanged }) {
    const isCreate = mode === 'create';
    const [u, setU] = useState(user);
    const [form, setForm] = useState(
        isCreate ? blankForm
            : { firstName: user.firstName, lastName: user.lastName, email: user.email, role: user.role, password: '' }
    );
    const [fieldErrors, setFieldErrors] = useState({});
    const [error, setError] = useState('');
    const [saving, setSaving] = useState(false);

    const [newPassword, setNewPassword] = useState('');
    const [pwMsg, setPwMsg] = useState('');
    const [pwBusy, setPwBusy] = useState(false);
    const [statusBusy, setStatusBusy] = useState(false);

    useEffect(() => {
        const onKey = (e) => { if (e.key === 'Escape') onClose(); };
        window.addEventListener('keydown', onKey);
        document.body.style.overflow = 'hidden';
        return () => { window.removeEventListener('keydown', onKey); document.body.style.overflow = ''; };
    }, [onClose]);

    const set = (f) => (e) => setForm(prev => ({ ...prev, [f]: e.target.value }));
    const err = (name) => fieldErrors[name]?.[0];

    async function save(e) {
        e.preventDefault();
        setError('');
        setFieldErrors({});
        setSaving(true);
        try {
            if (isCreate) {
                await createUser(form);
            } else {
                await updateUser(u.id, { firstName: form.firstName, lastName: form.lastName, email: form.email, role: form.role });
            }
            notifySuccess(isCreate
                ? `Account created for ${form.firstName} ${form.lastName}.`
                : `Account of ${form.firstName} ${form.lastName} updated.`);
            onChanged();
            onClose();
        } catch (ex) {
            setFieldErrors(ex.fieldErrors || {});
            if (!ex.fieldErrors || Object.keys(ex.fieldErrors).length === 0) setError(ex.message);
            notifyError(ex.message);
        } finally {
            setSaving(false);
        }
    }

    async function toggleActive() {
        setError('');
        setStatusBusy(true);
        try {
            const updated = await setUserActive(u.id, !u.isActive);
            setU(updated);
            notifySuccess(`Account ${updated.isActive ? 'reactivated' : 'deactivated'}.`);
            onChanged();
        } catch (ex) {
            setError(ex.message);
            notifyError(ex.message);
        } finally {
            setStatusBusy(false);
        }
    }

    async function doReset() {
        setError('');
        setPwMsg('');
        setPwBusy(true);
        try {
            await resetUserPassword(u.id, newPassword);
            setNewPassword('');
            setPwMsg('Password reset.');
            notifySuccess('Password reset.');
        } catch (ex) {
            setError(ex.fieldErrors?.newPassword?.[0] || ex.message);
            notifyError(ex.fieldErrors?.newPassword?.[0] || ex.message);
        } finally {
            setPwBusy(false);
        }
    }

    const isSelf = !isCreate && u.id === currentUserId;

    return createPortal(
        <div className="modal-overlay" onClick={onClose} role="presentation">
            <div className="modal" role="dialog" aria-modal="true" aria-labelledby="user-modal-title" onClick={e => e.stopPropagation()}>
                <header className="modal-head">
                    <h2 id="user-modal-title">{isCreate ? 'New user' : 'Edit user'}</h2>
                    <button type="button" className="modal-close" onClick={onClose} aria-label="Close">×</button>
                </header>

                <div className="modal-body">
                    {error && <div className="alert">{error}</div>}

                    <form id="user-form" onSubmit={save} noValidate>
                        <div className="field-row">
                            <div className="field">
                                <label htmlFor="firstName">First name</label>
                                <input id="firstName" value={form.firstName} onChange={set('firstName')} autoComplete="off" />
                                {err('firstName') && <p className="field-error">{err('firstName')}</p>}
                            </div>
                            <div className="field">
                                <label htmlFor="lastName">Last name</label>
                                <input id="lastName" value={form.lastName} onChange={set('lastName')} autoComplete="off" />
                                {err('lastName') && <p className="field-error">{err('lastName')}</p>}
                            </div>
                        </div>
                        <div className="field">
                            <label htmlFor="email">Email</label>
                            <input id="email" type="email" value={form.email} onChange={set('email')} autoComplete="off" />
                            {err('email') && <p className="field-error">{err('email')}</p>}
                        </div>
                        <div className="field">
                            <label htmlFor="role">Role</label>
                            <select id="role" value={form.role} onChange={set('role')}>
                                {roleOptions.map(r => <option key={r.value} value={r.value}>{r.label}</option>)}
                            </select>
                            {err('role') && <p className="field-error">{err('role')}</p>}
                        </div>
                        {isCreate && (
                            <div className="field">
                                <label htmlFor="password">Initial password</label>
                                <input id="password" type="text" value={form.password} onChange={set('password')} autoComplete="off"
                                    placeholder="At least 8 chars, letters and digits" />
                                {err('password') && <p className="field-error">{err('password')}</p>}
                            </div>
                        )}
                    </form>

                    {!isCreate && (
                        <>
                            <div className="user-modal-section">
                                <h4>Reset password</h4>
                                <div className="user-inline">
                                    <div className="field">
                                        <label htmlFor="newPassword">New password</label>
                                        <input id="newPassword" type="text" value={newPassword}
                                            onChange={e => setNewPassword(e.target.value)} autoComplete="off"
                                            placeholder="At least 8 chars, letters and digits" />
                                    </div>
                                    <button type="button" className="btn btn-ghost" disabled={pwBusy || !newPassword} onClick={doReset}>
                                        {pwBusy ? 'Resetting…' : 'Reset'}
                                    </button>
                                </div>
                                {pwMsg && <p className="field-error" style={{ color: 'var(--up)' }}>{pwMsg}</p>}
                            </div>

                            <div className="user-modal-section">
                                <h4>Account status</h4>
                                <div className="user-status-row">
                                    <p>
                                        This account is{' '}
                                        <strong>{u.isActive ? 'active' : 'inactive'}</strong>.
                                        {isSelf && ' (This is your account.)'}
                                    </p>
                                    {!isSelf && (
                                        <button
                                            type="button"
                                            className={u.isActive ? 'btn btn-danger' : 'btn btn-ghost'}
                                            disabled={statusBusy}
                                            onClick={toggleActive}
                                        >
                                            {statusBusy ? 'Working…' : u.isActive ? 'Deactivate' : 'Reactivate'}
                                        </button>
                                    )}
                                </div>
                            </div>
                        </>
                    )}
                </div>

                <footer className="modal-foot">
                    <button type="button" className="btn btn-ghost" onClick={onClose}>Cancel</button>
                    <button type="submit" form="user-form" className="btn btn-primary" disabled={saving}>
                        {saving && <span className="spinner" aria-hidden="true" />}
                        {saving ? 'Saving…' : isCreate ? 'Create user' : 'Save changes'}
                    </button>
                </footer>
            </div>
        </div>,
        document.body
    );
}

function UserManagementPage() {
    const { user: currentUser } = useAuth();
    const [rows, setRows] = useState([]);
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState(null);
    const [role, setRole] = useState('All');
    const [statusFilter, setStatusFilter] = useState('All');
    const [search, setSearch] = useState('');
    const [appliedSearch, setAppliedSearch] = useState('');
    const [reload, setReload] = useState(0);
    const [modal, setModal] = useState(null); // { mode, user }

    useEffect(() => {
        let active = true;
        (async () => {
            setLoading(true);
            setError(null);
            try {
                const data = await listUsers({ role, status: statusFilter, search: appliedSearch });
                if (active) setRows(data.users);
            } catch (err) {
                if (active) setError(err.message);
            } finally {
                if (active) setLoading(false);
            }
        })();
        return () => { active = false; };
    }, [role, statusFilter, appliedSearch, reload]);

    const refresh = () => setReload(r => r + 1);

    return (
        <div className="users-page">
            <header className="users-head">
                <div>
                    <h2>User management</h2>
                    <p className="users-sub">
                        Create, edit, and deactivate accounts across all six roles, and reset passwords
                        for locked-out users.
                    </p>
                </div>
                <div className="users-controls">
                    <form className="users-search" onSubmit={e => { e.preventDefault(); setAppliedSearch(search); }}>
                        <input type="search" placeholder="Search name or email" value={search} onChange={e => setSearch(e.target.value)} />
                    </form>
                    <label className="users-filter">
                        <span>Role</span>
                        <select value={role} onChange={e => setRole(e.target.value)}>
                            <option value="All">All</option>
                            {roleOptions.map(r => <option key={r.value} value={r.value}>{r.label}</option>)}
                        </select>
                    </label>
                    <label className="users-filter">
                        <span>Status</span>
                        <select value={statusFilter} onChange={e => setStatusFilter(e.target.value)}>
                            {['All', 'Active', 'Inactive'].map(s => <option key={s} value={s}>{s}</option>)}
                        </select>
                    </label>
                    <button className="btn btn-primary btn-sm" type="button" onClick={() => setModal({ mode: 'create' })}>
                        New user
                    </button>
                </div>
            </header>

            {error && <div className="alert">{error}</div>}

            {loading ? (
                <p className="users-empty">Loading…</p>
            ) : rows.length === 0 ? (
                <p className="users-empty">No users found.</p>
            ) : (
                <div className="card users-table-wrap">
                    <table className="users-table">
                        <thead>
                            <tr>
                                <th>Name</th>
                                <th>Email</th>
                                <th>Role</th>
                                <th>Status</th>
                                <th>Created</th>
                            </tr>
                        </thead>
                        <tbody>
                            {rows.map(u => (
                                <tr key={u.id} className="users-row" onClick={() => setModal({ mode: 'edit', user: u })}>
                                    <td><strong>{u.fullName}</strong></td>
                                    <td>{u.email}</td>
                                    <td><span className="chip chip-blue">{roleLabel(u.role)}</span></td>
                                    <td>
                                        <span className={u.isActive ? 'chip chip-muted' : 'chip chip-inactive'}>
                                            {u.isActive ? 'Active' : 'Inactive'}
                                        </span>
                                    </td>
                                    <td className="users-when">{formatPHT(u.createdAtUtc)}</td>
                                </tr>
                            ))}
                        </tbody>
                    </table>
                </div>
            )}

            {modal && (
                <UserModal
                    mode={modal.mode}
                    user={modal.user}
                    currentUserId={currentUser?.id}
                    onClose={() => setModal(null)}
                    onChanged={refresh}
                />
            )}
        </div>
    );
}

export default UserManagementPage;
