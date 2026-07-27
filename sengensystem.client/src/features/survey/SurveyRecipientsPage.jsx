import { useEffect, useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import { notifySuccess, notifyError } from '../shell/notify';
import { confirmAction } from '../shell/confirm';
import { useTableControls } from '../shell/useTableControls';
import { SortHeader, Pagination } from '../shell/tableControls';
import { getAudience, sendInvitations, remindInvitations, withdrawInvitation, getCollection } from './api';
import './survey.css';

/* Super Admin page for choosing exactly who answers the ISO/IEC 25010 rating survey. This is a
   full page rather than a modal: picking an audience out of every account needs search, filters,
   sorting and paging, and the Super Admin returns to it repeatedly to nudge or withdraw people. */

const ROLES = [
    { value: 'Student', label: 'Students' },
    { value: 'FacultyMember', label: 'Faculty' },
    { value: 'AdmissionOfficer', label: 'Admission Officers' },
    { value: 'Registrar', label: 'Registrars' },
    { value: 'AcademicHead', label: 'Academic Heads' },
    { value: 'SchoolAdmin', label: 'School Admins' }
];

const STATUS_LABEL = {
    'not-invited': ['Not invited', 'chip'],
    pending: ['Invited · awaiting reply', 'chip chip-yellow'],
    answered: ['Answered', 'chip chip-blue']
};

function fmt(iso) {
    if (!iso) return '—';
    const d = new Date(iso);
    return Number.isNaN(d.getTime()) ? iso : d.toLocaleString('en-PH', {
        timeZone: 'Asia/Manila', month: 'short', day: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit', hour12: true
    });
}

function SurveyRecipientsPage() {
    const [users, setUsers] = useState(null);
    const [collection, setCollectionState] = useState(null);
    const [selected, setSelected] = useState(() => new Set());
    const [search, setSearch] = useState('');
    const [roleFilter, setRoleFilter] = useState('');
    const [statusFilter, setStatusFilter] = useState('');
    const [note, setNote] = useState('');
    const [pushNotification, setPushNotification] = useState(true);
    const [sendEmail, setSendEmail] = useState(true);
    const [busy, setBusy] = useState('');

    const [reload, setReload] = useState(0);

    useEffect(() => {
        let active = true;
        (async () => {
            try {
                const [audience, coll] = await Promise.all([getAudience(), getCollection()]);
                if (!active) return;
                setUsers(audience.users);
                setCollectionState(coll);
            } catch (err) {
                if (active) notifyError(err.message);
            }
        })();
        return () => { active = false; };
    }, [reload]);

    // Filters run before the table controls so sorting and paging see the visible set.
    const filtered = useMemo(() => {
        const q = search.trim().toLowerCase();
        return (users ?? []).filter(u => {
            if (roleFilter && u.role !== roleFilter) return false;
            if (statusFilter && u.status !== statusFilter) return false;
            if (!q) return true;
            return u.name.toLowerCase().includes(q) || u.email.toLowerCase().includes(q);
        });
    }, [users, search, roleFilter, statusFilter]);

    const table = useTableControls(filtered, {
        columns: {
            name: r => r.name,
            role: r => r.role,
            email: r => r.email,
            status: r => r.status,
            sent: r => r.sentAtUtc ?? ''
        },
        initialSort: { key: 'name', dir: 'asc' },
        initialPageSize: 25
    });

    const toggle = id => setSelected(prev => {
        const next = new Set(prev);
        if (next.has(id)) next.delete(id); else next.add(id);
        return next;
    });

    // "Select all" acts on everything the current filters show, not just the visible page —
    // that is what "invite all the students" actually means.
    const allFilteredSelected = filtered.length > 0 && filtered.every(u => selected.has(u.id));
    const toggleAllFiltered = () => setSelected(prev => {
        const next = new Set(prev);
        if (allFilteredSelected) filtered.forEach(u => next.delete(u.id));
        else filtered.forEach(u => next.add(u.id));
        return next;
    });

    const selectedUsers = useMemo(
        () => (users ?? []).filter(u => selected.has(u.id)),
        [users, selected]
    );
    const selectedPending = selectedUsers.filter(u => u.status === 'pending');
    const closed = collection && !collection.isOpen;

    async function send() {
        if (selected.size === 0) return;
        const answered = selectedUsers.filter(u => u.status === 'answered').length;
        const ok = await confirmAction({
            title: `Send the survey to ${selected.size} user(s)?`,
            message:
                `${pushNotification ? 'A notification will appear on their bell' : 'No in-app notification'}` +
                `${pushNotification && sendEmail ? ' and ' : sendEmail ? '. ' : '. '}` +
                `${sendEmail ? 'a unique survey link will be emailed to them' : 'no email will be sent'}.` +
                (answered > 0 ? ` ${answered} already answered and will be skipped.` : ''),
            confirmLabel: 'Send survey'
        });
        if (!ok) return;

        setBusy('send');
        try {
            const res = await sendInvitations({
                userIds: [...selected], note, pushNotification, sendEmail
            });
            notifySuccess(
                `Sent to ${res.created + res.resent} user(s) — ${res.created} new, ${res.resent} resent, ${res.skipped} already answered.`
            );
            setSelected(new Set());
            setNote('');
            setReload(v => v + 1);
        } catch (err) {
            notifyError(err.message);
        } finally {
            setBusy('');
        }
    }

    async function remind() {
        const ids = selectedPending.map(u => u.invitationId).filter(Boolean);
        const everyone = ids.length === 0;
        const ok = await confirmAction({
            title: everyone ? 'Remind everyone still pending?' : `Remind ${ids.length} selected user(s)?`,
            message: everyone
                ? 'Every invited person who has not answered yet gets another notification.'
                : 'The selected pending respondents get another notification.',
            confirmLabel: 'Send reminder'
        });
        if (!ok) return;

        setBusy('remind');
        try {
            const res = await remindInvitations({ invitationIds: ids, note, pushNotification, sendEmail });
            notifySuccess(res.reminded === 0 ? 'Nobody is pending — no reminders sent.' : `Reminded ${res.reminded} respondent(s).`);
            setReload(v => v + 1);
        } catch (err) {
            notifyError(err.message);
        } finally {
            setBusy('');
        }
    }

    async function withdraw(user) {
        const ok = await confirmAction({
            title: `Withdraw ${user.name}'s invitation?`,
            message: 'Their survey link stops working and the notification no longer leads anywhere. You can invite them again later.',
            confirmLabel: 'Withdraw',
            danger: true
        });
        if (!ok) return;
        try {
            await withdrawInvitation(user.invitationId);
            notifySuccess(`Withdrew the invitation for ${user.name}.`);
            setReload(v => v + 1);
        } catch (err) {
            notifyError(err.message);
        }
    }

    const counts = useMemo(() => {
        const list = users ?? [];
        return {
            total: list.length,
            invited: list.filter(u => u.status !== 'not-invited').length,
            answered: list.filter(u => u.status === 'answered').length
        };
    }, [users]);

    return (
        <div className="setup-page">
            <header className="setup-head">
                <div>
                    <h2>Survey recipients</h2>
                    <p className="setup-sub">
                        Choose exactly who receives the ISO/IEC 25010 evaluation. Pick people below, then push it to
                        their notification bell and/or email them a unique link.
                    </p>
                </div>
                <Link className="btn btn-ghost" to="/survey-admin">View results dashboard</Link>
            </header>

            {closed && (
                <div className="alert survey-alert">
                    Collection is currently <strong>closed</strong> — reopen it on the
                    {' '}<Link to="/survey-admin">results dashboard</Link> before inviting more respondents.
                </div>
            )}

            <section className="card param-card">
                <div className="survey-stats">
                    <div><strong>{counts.total}</strong><span>Active accounts</span></div>
                    <div><strong>{counts.invited}</strong><span>Invited</span></div>
                    <div><strong>{counts.answered}</strong><span>Answered</span></div>
                    <div><strong>{selected.size}</strong><span>Selected now</span></div>
                </div>
            </section>

            <section className="card param-card">
                <h3>1 · Pick the recipients</h3>
                <div className="survey-filters">
                    <label className="survey-field">
                        <span>Search</span>
                        <input
                            value={search}
                            onChange={e => setSearch(e.target.value)}
                            placeholder="Name or email"
                        />
                    </label>
                    <label className="survey-field">
                        <span>Role</span>
                        <select value={roleFilter} onChange={e => setRoleFilter(e.target.value)}>
                            <option value="">All roles</option>
                            {ROLES.map(r => <option key={r.value} value={r.value}>{r.label}</option>)}
                        </select>
                    </label>
                    <label className="survey-field">
                        <span>Status</span>
                        <select value={statusFilter} onChange={e => setStatusFilter(e.target.value)}>
                            <option value="">Any status</option>
                            <option value="not-invited">Not invited</option>
                            <option value="pending">Invited · awaiting reply</option>
                            <option value="answered">Answered</option>
                        </select>
                    </label>
                    <div className="survey-filter-actions">
                        <button type="button" className="btn btn-ghost btn-sm" onClick={toggleAllFiltered} disabled={filtered.length === 0}>
                            {allFilteredSelected ? 'Clear these' : `Select all ${filtered.length}`}
                        </button>
                        <button type="button" className="btn btn-ghost btn-sm" onClick={() => setSelected(new Set())} disabled={selected.size === 0}>
                            Clear selection
                        </button>
                    </div>
                </div>

                {!users ? <p className="setup-empty">Loading…</p> : filtered.length === 0 ? (
                    <p className="setup-empty">No accounts match these filters.</p>
                ) : (
                    <>
                        <div className="setup-table-wrap">
                            <table className="setup-table">
                                <thead>
                                    <tr>
                                        <th className="survey-check-col">
                                            <input
                                                type="checkbox"
                                                checked={allFilteredSelected}
                                                onChange={toggleAllFiltered}
                                                aria-label="Select all filtered users"
                                            />
                                        </th>
                                        <SortHeader label="Name" sortKey="name" sort={table.sort} onSort={table.toggleSort} />
                                        <SortHeader label="Role" sortKey="role" sort={table.sort} onSort={table.toggleSort} />
                                        <SortHeader label="Email" sortKey="email" sort={table.sort} onSort={table.toggleSort} />
                                        <SortHeader label="Status" sortKey="status" sort={table.sort} onSort={table.toggleSort} />
                                        <SortHeader label="Last sent" sortKey="sent" sort={table.sort} onSort={table.toggleSort} />
                                        <th>Nudges</th>
                                        <th />
                                    </tr>
                                </thead>
                                <tbody>
                                    {table.pageRows.map(u => {
                                        const [label, cls] = STATUS_LABEL[u.status] ?? STATUS_LABEL['not-invited'];
                                        return (
                                            <tr key={u.id} className={selected.has(u.id) ? 'survey-row-picked' : undefined}>
                                                <td className="survey-check-col">
                                                    <input
                                                        type="checkbox"
                                                        checked={selected.has(u.id)}
                                                        onChange={() => toggle(u.id)}
                                                        aria-label={`Select ${u.name}`}
                                                    />
                                                </td>
                                                <td><strong>{u.name}</strong></td>
                                                <td className="setup-muted">{u.role}</td>
                                                <td className="setup-muted">{u.email}</td>
                                                <td><span className={cls}>{label}</span></td>
                                                <td className="setup-muted">{fmt(u.sentAtUtc)}</td>
                                                <td className="setup-muted">{u.reminderCount || '—'}</td>
                                                <td>
                                                    {u.status === 'pending' && (
                                                        <button type="button" className="btn btn-ghost btn-sm" onClick={() => withdraw(u)}>
                                                            Withdraw
                                                        </button>
                                                    )}
                                                </td>
                                            </tr>
                                        );
                                    })}
                                </tbody>
                            </table>
                        </div>
                        <Pagination {...table} />
                    </>
                )}
            </section>

            <section className="card param-card">
                <h3>2 · How they hear about it</h3>
                <div className="survey-roles">
                    <label className="survey-role">
                        <input type="checkbox" checked={pushNotification} onChange={e => setPushNotification(e.target.checked)} />
                        <span>Push an in-app notification to their bell</span>
                    </label>
                    <label className="survey-role">
                        <input type="checkbox" checked={sendEmail} onChange={e => setSendEmail(e.target.checked)} />
                        <span>Email them a unique one-time link</span>
                    </label>
                </div>
                <label className="survey-field">
                    <span>Message (optional) — shown on the notification</span>
                    <textarea
                        rows={2}
                        maxLength={500}
                        value={note}
                        onChange={e => setNote(e.target.value)}
                        placeholder="e.g. Please answer before Friday. Maraming salamat!"
                    />
                </label>

                <div className="survey-actions">
                    <button
                        type="button"
                        className="btn btn-primary"
                        disabled={busy !== '' || selected.size === 0 || closed || (!pushNotification && !sendEmail)}
                        onClick={send}
                    >
                        {busy === 'send' && <span className="spinner" aria-hidden="true" />}
                        {busy === 'send' ? 'Sending…' : `Send survey to ${selected.size} selected`}
                    </button>
                    <button
                        type="button"
                        className="btn btn-ghost"
                        disabled={busy !== '' || closed}
                        onClick={remind}
                    >
                        {busy === 'remind' && <span className="spinner" aria-hidden="true" />}
                        {selectedPending.length > 0 ? `Remind ${selectedPending.length} pending` : 'Remind everyone pending'}
                    </button>
                </div>
                {!pushNotification && !sendEmail && (
                    <p className="setup-empty">Pick at least one way to reach them.</p>
                )}
            </section>
        </div>
    );
}

export default SurveyRecipientsPage;
