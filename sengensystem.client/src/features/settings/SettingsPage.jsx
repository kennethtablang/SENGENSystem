import { useEffect, useRef, useState } from 'react';
import { useAuth } from '../auth/useAuth';
import { Icon } from '../shell/AppLayout';
import { searchableItems } from '../shell/nav';
import { defaultPrefs, getPrefs, savePrefs } from './prefs';
import './settings.css';

function Segmented({ ariaLabel, value, options, onChange }) {
    return (
        <div className="seg" role="radiogroup" aria-label={ariaLabel}>
            {options.map(opt => (
                <button
                    key={opt.value}
                    type="button"
                    role="radio"
                    aria-checked={value === opt.value}
                    className={`seg-btn${value === opt.value ? ' active' : ''}`}
                    onClick={() => onChange(opt.value)}
                >
                    <span>{opt.label}</span>
                    {opt.hint && <small>{opt.hint}</small>}
                </button>
            ))}
        </div>
    );
}

function Toggle({ label, checked, onChange }) {
    return (
        <button
            type="button"
            role="switch"
            aria-checked={checked}
            aria-label={label}
            className={`switch${checked ? ' on' : ''}`}
            onClick={onChange}
        >
            <span className="switch-knob" />
        </button>
    );
}

function SettingRow({ title, desc, children }) {
    return (
        <div className="setting-row">
            <div className="setting-info">
                <strong>{title}</strong>
                <p>{desc}</p>
            </div>
            {children}
        </div>
    );
}

const TABS = [
    { id: 'appearance', label: 'Appearance', icon: 'sliders' },
    { id: 'behavior', label: 'Behavior', icon: 'bolt' },
    { id: 'schedule', label: 'Schedule', icon: 'calendar' },
    { id: 'device', label: 'This device', icon: 'gear' }
];

function SettingsPage() {
    const { user } = useAuth();
    const [tab, setTab] = useState('appearance');
    const [prefs, setPrefs] = useState(getPrefs);
    const [sidebarCollapsed, setSidebarCollapsed] = useState(
        () => localStorage.getItem('sengen.sidebar') === 'collapsed'
    );
    const [saved, setSaved] = useState(false);
    const savedTimer = useRef(null);

    useEffect(() => () => clearTimeout(savedTimer.current), []);

    const flashSaved = () => {
        setSaved(true);
        clearTimeout(savedTimer.current);
        savedTimer.current = setTimeout(() => setSaved(false), 1800);
    };

    const set = patch => {
        const next = { ...prefs, ...patch };
        setPrefs(next);
        savePrefs(next);
        flashSaved();
    };

    const setSidebar = collapsed => {
        localStorage.setItem('sengen.sidebar', collapsed ? 'collapsed' : 'expanded');
        window.dispatchEvent(new Event('sengen:sidebar'));
        setSidebarCollapsed(collapsed);
        flashSaved();
    };

    const reset = () => {
        setPrefs(defaultPrefs);
        savePrefs(defaultPrefs);
        setSidebar(false);
    };

    // Pages this role can open — offered as post-sign-in landing choices.
    const landingChoices = searchableItems(user.role);

    return (
        <div className="settings-page">
            <header className="settings-head rise">
                <div>
                    <h2>Settings</h2>
                    <p>Tune how SEN-GEN looks and behaves on this device. Changes apply instantly.</p>
                </div>
                <span className={`chip chip-blue settings-saved${saved ? ' show' : ''}`} aria-live="polite">
                    {saved ? 'Preferences saved' : ''}
                </span>
            </header>

            <div className="settings-layout rise rise-1">
                <nav className="settings-tabs" aria-label="Settings sections">
                    {TABS.map(t => (
                        <button
                            key={t.id}
                            type="button"
                            className={`settings-tab${tab === t.id ? ' active' : ''}`}
                            aria-current={tab === t.id ? 'page' : undefined}
                            onClick={() => setTab(t.id)}
                        >
                            <Icon name={t.icon} />
                            <span>{t.label}</span>
                        </button>
                    ))}
                </nav>

                <div className="settings-panels">
                    {tab === 'appearance' && (
                        <section className="card settings-card">
                            <h3>Appearance</h3>
                            <p className="settings-card-sub">Theme, layout density, and navigation.</p>

                            <SettingRow
                                title="Theme"
                                desc="Dark keeps the STI identity on a deep navy surface. System follows your operating system's setting."
                            >
                                <Segmented
                                    ariaLabel="Theme"
                                    value={prefs.theme}
                                    onChange={theme => set({ theme })}
                                    options={[
                                        { value: 'light', label: 'Light' },
                                        { value: 'dark', label: 'Dark' },
                                        { value: 'system', label: 'System' }
                                    ]}
                                />
                            </SettingRow>

                            <SettingRow
                                title="Interface density"
                                desc="Compact shrinks text and spacing across every page — handy when reviewing long registration or schedule tables."
                            >
                                <Segmented
                                    ariaLabel="Interface density"
                                    value={prefs.density}
                                    onChange={density => set({ density })}
                                    options={[
                                        { value: 'comfortable', label: 'Comfortable', hint: 'Default' },
                                        { value: 'compact', label: 'Compact', hint: 'Fits more' }
                                    ]}
                                />
                            </SettingRow>

                            <SettingRow
                                title="Collapsed sidebar"
                                desc="Keep the navigation rail minimized to icons. You can still expand it any time with the chevron button."
                            >
                                <Toggle
                                    label="Collapsed sidebar"
                                    checked={sidebarCollapsed}
                                    onChange={() => setSidebar(!sidebarCollapsed)}
                                />
                            </SettingRow>
                        </section>
                    )}

                    {tab === 'behavior' && (
                        <section className="card settings-card">
                            <h3>Behavior</h3>
                            <p className="settings-card-sub">Motion, confirmations, and notifications.</p>

                            <SettingRow
                                title="Reduce animations"
                                desc="Turns off entrance animations, hover lifts, and other movement. Your operating system's reduced-motion setting is always honored regardless of this switch."
                            >
                                <Toggle
                                    label="Reduce animations"
                                    checked={prefs.motion === 'reduced'}
                                    onChange={() => set({ motion: prefs.motion === 'reduced' ? 'full' : 'reduced' })}
                                />
                            </SettingRow>

                            <SettingRow
                                title="Confirm before deleting"
                                desc="Ask “are you sure?” before anything destructive. Turning this off makes delete buttons act immediately — signing out always asks."
                            >
                                <Toggle
                                    label="Confirm before deleting"
                                    checked={prefs.confirmDelete}
                                    onChange={() => set({ confirmDelete: !prefs.confirmDelete })}
                                />
                            </SettingRow>

                            <SettingRow
                                title="Notification duration"
                                desc="How long the small pop-up notifications stay on screen after a save, delete, or error."
                            >
                                <Segmented
                                    ariaLabel="Notification duration"
                                    value={prefs.toastDuration}
                                    onChange={toastDuration => set({ toastDuration })}
                                    options={[
                                        { value: 'short', label: 'Short', hint: '2 s' },
                                        { value: 'normal', label: 'Normal', hint: '3.5 s' },
                                        { value: 'long', label: 'Long', hint: '6 s' }
                                    ]}
                                />
                            </SettingRow>

                            <SettingRow
                                title="Start page after sign-in"
                                desc="The page SEN-GEN opens right after you sign in on this device."
                            >
                                <select
                                    aria-label="Start page after sign-in"
                                    value={prefs.landing}
                                    onChange={e => set({ landing: e.target.value })}
                                >
                                    {landingChoices.map(item => (
                                        <option key={item.to} value={item.to}>{item.label}</option>
                                    ))}
                                </select>
                            </SettingRow>
                        </section>
                    )}

                    {tab === 'schedule' && (
                        <section className="card settings-card">
                            <h3>Schedule display</h3>
                            <p className="settings-card-sub">How times read on the timetable calendars and class lists.</p>

                            <SettingRow
                                title="Time format"
                                desc="Applies to My schedule, the Schedule board, and day-by-day class lists."
                            >
                                <Segmented
                                    ariaLabel="Time format"
                                    value={prefs.timeFormat}
                                    onChange={timeFormat => set({ timeFormat })}
                                    options={[
                                        { value: '24h', label: '24-hour', hint: '13:00' },
                                        { value: '12h', label: '12-hour', hint: '1:00 PM' }
                                    ]}
                                />
                            </SettingRow>
                        </section>
                    )}

                    {tab === 'device' && (
                        <section className="card settings-card">
                            <h3>This device</h3>
                            <p className="settings-card-sub">
                                Preferences are saved in this browser only — they do not follow your account to other
                                computers. Your name, email, and password live in <a href="/profile">Profile settings</a>.
                            </p>
                            <button type="button" className="btn btn-ghost" onClick={reset}>
                                Reset to defaults
                            </button>
                        </section>
                    )}
                </div>
            </div>
        </div>
    );
}

export default SettingsPage;
