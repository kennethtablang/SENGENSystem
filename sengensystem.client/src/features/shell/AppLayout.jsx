import { useEffect, useMemo, useRef, useState } from 'react';
import { NavLink, Outlet, useNavigate } from 'react-router-dom';
import { useAuth } from '../auth/useAuth';
import { Wordmark } from '../auth/AuthLayout';
import NotificationsBell from '../notifications/NotificationsBell';
import EnrollmentTicker from '../enrollment-stage/EnrollmentTicker';
import { confirmAction } from './confirm';
import { iconPaths, searchableItems, visibleGroups, visibleMinor } from './nav';
import './shell.css';

const roleLabels = {
    Student: 'Student',
    FacultyMember: 'Faculty Member',
    AdmissionOfficer: 'Admission Officer',
    Registrar: 'Registrar',
    AcademicHead: 'Academic Head',
    SchoolAdmin: 'School Admin'
};

export function Icon({ name }) {
    return (
        <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor"
            strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
            <path d={iconPaths[name]} />
        </svg>
    );
}

/* Type-to-jump: finds any function the current role can open. */
function FunctionSearch({ role }) {
    const navigate = useNavigate();
    const boxRef = useRef(null);
    const [q, setQ] = useState('');

    const items = useMemo(() => searchableItems(role), [role]);
    const matches = q.trim()
        ? items.filter(i => i.label.toLowerCase().includes(q.trim().toLowerCase())).slice(0, 6)
        : [];

    const go = item => {
        setQ('');
        navigate(item.to);
    };

    useEffect(() => {
        if (!q) return undefined;
        const onDoc = e => {
            if (boxRef.current && !boxRef.current.contains(e.target)) setQ('');
        };
        document.addEventListener('mousedown', onDoc);
        return () => document.removeEventListener('mousedown', onDoc);
    }, [q]);

    return (
        <div className="shell-search" ref={boxRef}>
            <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor"
                strokeWidth="2" strokeLinecap="round" aria-hidden="true">
                <path d="M11 19a8 8 0 1 0 0-16 8 8 0 0 0 0 16M21 21l-4.35-4.35" />
            </svg>
            <input
                type="text"
                placeholder="Find a function…"
                aria-label="Find a function"
                value={q}
                onChange={e => setQ(e.target.value)}
                onKeyDown={e => {
                    if (e.key === 'Enter' && matches.length > 0) go(matches[0]);
                    if (e.key === 'Escape') setQ('');
                }}
            />
            {matches.length > 0 && (
                <div className="shell-search-results" role="listbox">
                    {matches.map(item => (
                        <button type="button" key={item.to} onClick={() => go(item)} role="option" aria-selected="false">
                            <Icon name={item.icon} />
                            <span>{item.label}</span>
                        </button>
                    ))}
                </div>
            )}
        </div>
    );
}

/* Avatar dropdown: profile, settings, help, sign out. */
function UserMenu({ user, logout }) {
    const menuRef = useRef(null);
    const [open, setOpen] = useState(false);

    const initials = `${user.firstName[0] ?? ''}${user.lastName[0] ?? ''}`.toUpperCase();

    useEffect(() => {
        if (!open) return undefined;
        const onDoc = e => {
            if (menuRef.current && !menuRef.current.contains(e.target)) setOpen(false);
        };
        const onKey = e => {
            if (e.key === 'Escape') setOpen(false);
        };
        document.addEventListener('mousedown', onDoc);
        document.addEventListener('keydown', onKey);
        return () => {
            document.removeEventListener('mousedown', onDoc);
            document.removeEventListener('keydown', onKey);
        };
    }, [open]);

    return (
        <div className="user-menu" ref={menuRef}>
            <button
                type="button"
                className="user-menu-btn"
                onClick={() => setOpen(o => !o)}
                aria-haspopup="menu"
                aria-expanded={open}
            >
                <span className="avatar">{initials}</span>
                <span className="user-menu-name">{user.firstName} {user.lastName}</span>
                <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor"
                    strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"
                    style={{ transform: open ? 'rotate(180deg)' : 'none', transition: 'transform 0.2s ease' }}>
                    <path d="m6 9 6 6 6-6" />
                </svg>
            </button>

            {open && (
                <div className="user-menu-drop" role="menu">
                    <div className="user-menu-head">
                        <span className="avatar">{initials}</span>
                        <div>
                            <strong>{user.firstName} {user.lastName}</strong>
                            <small>{user.email}</small>
                        </div>
                    </div>
                    <NavLink to="/profile" role="menuitem" onClick={() => setOpen(false)}>
                        <Icon name="user" /> Profile settings
                    </NavLink>
                    <NavLink to="/settings" role="menuitem" onClick={() => setOpen(false)}>
                        <Icon name="sliders" /> Settings
                    </NavLink>
                    <NavLink to="/help" role="menuitem" onClick={() => setOpen(false)}>
                        <Icon name="help" /> Help &amp; about
                    </NavLink>
                    <button type="button" className="user-menu-signout" role="menuitem" onClick={logout}>
                        <Icon name="signout" /> Sign out
                    </button>
                </div>
            )}
        </div>
    );
}

function AppLayout() {
    const { user, logout } = useAuth();

    // Second thought before ending the session — mirrors the delete confirmations.
    const confirmLogout = async () => {
        const ok = await confirmAction({
            title: 'Sign out of SEN-GEN?',
            message: 'You’ll need to sign in again to keep working.',
            confirmLabel: 'Sign out'
        });
        if (ok) logout();
    };

    const [navOpen, setNavOpen] = useState(false);
    const [collapsed, setCollapsed] = useState(() => localStorage.getItem('sengen.sidebar') === 'collapsed');

    // Close the off-canvas sidebar when a nav link is used (mobile).
    const closeNav = () => setNavOpen(false);

    // The Settings page can flip the sidebar preference too — stay in sync with it.
    useEffect(() => {
        const sync = () => setCollapsed(localStorage.getItem('sengen.sidebar') === 'collapsed');
        window.addEventListener('sengen:sidebar', sync);
        return () => window.removeEventListener('sengen:sidebar', sync);
    }, []);

    const toggleCollapsed = () => {
        setCollapsed(prev => {
            localStorage.setItem('sengen.sidebar', prev ? 'expanded' : 'collapsed');
            return !prev;
        });
    };

    const groups = visibleGroups(user.role);
    const minor = visibleMinor(user.role);
    // Tooltips carry the labels once they are hidden by the collapsed rail.
    const tip = label => (collapsed ? label : undefined);

    return (
        <div className="shell">
            {navOpen && <div className="shell-scrim" onClick={() => setNavOpen(false)} aria-hidden="true" />}

            <aside className={`sidebar${navOpen ? ' open' : ''}${collapsed ? ' collapsed' : ''}`}>
                <div className="sidebar-brand">
                    <Wordmark />
                    <button
                        type="button"
                        className="sidebar-toggle"
                        onClick={toggleCollapsed}
                        aria-label={collapsed ? 'Expand sidebar' : 'Collapse sidebar'}
                        title={collapsed ? 'Expand sidebar' : 'Collapse sidebar'}
                    >
                        <Icon name="chevrons" />
                    </button>
                </div>

                <nav className="sidebar-nav" aria-label="Main navigation">
                    {groups.map(group => (
                        <div className="nav-group" key={group.label}>
                            <span className="nav-group-label">{group.label}</span>
                            {group.items.map(item => (
                                <NavLink
                                    key={item.to}
                                    to={item.to}
                                    end={item.end}
                                    onClick={closeNav}
                                    title={tip(item.label)}
                                    className={({ isActive }) => `nav-item${isActive ? ' active' : ''}`}
                                >
                                    <Icon name={item.icon} />
                                    <span>{item.label}</span>
                                </NavLink>
                            ))}
                        </div>
                    ))}
                </nav>

                <div className="sidebar-foot">
                    {minor.map(item => (
                        <NavLink
                            key={item.to}
                            to={item.to}
                            onClick={closeNav}
                            title={tip(item.label)}
                            className={({ isActive }) => `nav-item${isActive ? ' active' : ''}`}
                        >
                            <Icon name={item.icon} />
                            <span>{item.label}</span>
                        </NavLink>
                    ))}
                    <button type="button" className="nav-item nav-signout" onClick={confirmLogout} title={tip('Sign out')}>
                        <Icon name="signout" />
                        <span>Sign out</span>
                    </button>
                </div>
            </aside>

            <div className="shell-main">
                <header className="shell-top">
                    <button
                        type="button"
                        className="shell-menu-btn"
                        onClick={() => setNavOpen(true)}
                        aria-label="Open navigation"
                    >
                        <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor"
                            strokeWidth="2" strokeLinecap="round" aria-hidden="true">
                            <path d="M3 6h18M3 12h18M3 18h18" />
                        </svg>
                    </button>

                    <EnrollmentTicker />

                    <div className="shell-user">
                        <FunctionSearch role={user.role} />
                        <NotificationsBell />
                        <span className="chip chip-blue">{roleLabels[user.role] ?? user.role}</span>
                        <UserMenu user={user} logout={confirmLogout} />
                    </div>
                </header>

                <main className="shell-content">
                    <Outlet />
                </main>
            </div>
        </div>
    );
}

export default AppLayout;
