// Application preferences (Settings page). Stored in this browser's localStorage and
// applied as data-attributes on <html> so plain CSS can react to them — no re-render
// plumbing needed. Server round-trips are deliberately avoided: these are device-level
// display choices, not account data.

const KEY = 'sengen.prefs';

export const defaultPrefs = {
    theme: 'light',           // 'light' | 'dark' | 'system'
    density: 'comfortable',   // 'comfortable' | 'compact'
    motion: 'full',           // 'full' | 'reduced'
    timeFormat: '24h',        // '24h' | '12h'
    landing: '/',             // route opened right after sign-in
    confirmDelete: true,      // ask "are you sure?" before destructive actions
    toastDuration: 'normal'   // 'short' | 'normal' | 'long' — CRUD toast lifetime
};

export function getPrefs() {
    try {
        return { ...defaultPrefs, ...JSON.parse(localStorage.getItem(KEY) ?? '{}') };
    } catch {
        return { ...defaultPrefs };
    }
}

export function savePrefs(next) {
    localStorage.setItem(KEY, JSON.stringify(next));
    applyPrefs(next);
}

const systemDark = window.matchMedia('(prefers-color-scheme: dark)');

// Re-stamp the theme when the OS theme flips and the user chose "system".
systemDark.addEventListener('change', () => {
    if (getPrefs().theme === 'system') applyPrefs();
});

function resolveTheme(theme) {
    return theme === 'system' ? (systemDark.matches ? 'dark' : 'light') : theme;
}

/* Stamp the active preferences onto the root element (called at startup and on change). */
export function applyPrefs(prefs = getPrefs()) {
    const root = document.documentElement;
    root.dataset.theme = resolveTheme(prefs.theme);
    root.dataset.density = prefs.density;
    root.dataset.motion = prefs.motion;
}

export function uses12HourTime() {
    return getPrefs().timeFormat === '12h';
}

// Toast lifetime in ms, from the Settings preference (used by shell/notify.js).
export function toastAutoClose() {
    switch (getPrefs().toastDuration) {
        case 'short': return 2000;
        case 'long': return 6000;
        default: return 3500;
    }
}

// Whether destructive actions should ask for confirmation first (shell/confirm.jsx).
export function confirmsDeletes() {
    return getPrefs().confirmDelete !== false;
}
