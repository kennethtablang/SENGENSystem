// Application preferences (Settings page). Stored in this browser's localStorage and
// applied as data-attributes on <html> so plain CSS can react to them — no re-render
// plumbing needed. Server round-trips are deliberately avoided: these are device-level
// display choices, not account data.

const KEY = 'sengen.prefs';

export const defaultPrefs = {
    // Appearance
    theme: 'light',            // 'light' | 'dark' | 'system'
    accent: 'blue',            // 'blue' | 'teal' | 'violet' | 'crimson' — interactive/highlight hue
    density: 'comfortable',    // 'comfortable' | 'compact'
    fontScale: 'medium',       // 'small' | 'medium' | 'large' — base type size
    corners: 'rounded',        // 'rounded' | 'square' — UI corner radius
    contrast: 'normal',        // 'normal' | 'high' — stronger borders/text
    transparency: 'on',        // 'on' | 'off' — 'off' makes translucent surfaces opaque
    focusRing: 'default',      // 'default' | 'bold' — keyboard focus outline weight

    // Tables & reading
    tableStripes: 'on',        // 'on' | 'off' — zebra striping on data tables
    stickyHeaders: 'on',       // 'on' | 'off' — keep table headers visible while scrolling

    // Motion & behavior
    motion: 'full',            // 'full' | 'reduced'
    confirmDelete: true,       // ask before destructive actions
    confirmSignout: true,      // ask before signing out
    confirmHeavy: true,        // ask before heavy actions (generate schedule, bulk reminders)
    toastDuration: 'normal',   // 'short' | 'normal' | 'long' — toast lifetime
    toastPosition: 'bottom-right', // 'top-right' | 'bottom-right' | 'bottom-left' | 'top-center'
    landing: '/',              // route opened right after sign-in

    // Notifications
    showBadges: true,          // show unread/pending number badges (bell + sidebar)
    bellRefresh: 'normal',     // 'realtime' | 'normal' | 'relaxed' — bell/badge poll cadence
    markReadOnOpen: true,      // opening a notice marks it read

    // Schedule display
    timeFormat: '24h'          // '24h' | '12h'
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
    root.dataset.accent = prefs.accent;
    root.dataset.density = prefs.density;
    root.dataset.font = prefs.fontScale;
    root.dataset.corners = prefs.corners;
    root.dataset.contrast = prefs.contrast;
    root.dataset.transparency = prefs.transparency;
    root.dataset.focus = prefs.focusRing;
    root.dataset.stripes = prefs.tableStripes;
    root.dataset.sticky = prefs.stickyHeaders;
    root.dataset.motion = prefs.motion;
    root.dataset.toastPos = prefs.toastPosition;
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

// Where toasts appear (used by shell/notify.js).
export function toastPosition() {
    return getPrefs().toastPosition || 'bottom-right';
}

// Whether destructive actions should ask for confirmation first (shell/confirm.jsx).
export function confirmsDeletes() {
    return getPrefs().confirmDelete !== false;
}

// Whether signing out asks for confirmation first (shell/AppLayout.jsx).
export function confirmsSignout() {
    return getPrefs().confirmSignout !== false;
}

// Whether heavy actions (generate schedule, bulk reminders) ask first.
export function confirmsHeavy() {
    return getPrefs().confirmHeavy !== false;
}

// Whether numbered badges (bell + sidebar) are shown at all.
export function showsBadges() {
    return getPrefs().showBadges !== false;
}

// Bell / badge poll interval in ms — 'realtime' still keeps a slow fallback poll.
export function bellRefreshMs() {
    switch (getPrefs().bellRefresh) {
        case 'realtime': return 20_000;
        case 'relaxed': return 300_000;
        default: return 60_000;
    }
}

// Whether opening a notice marks it read (shell/NotificationsBell.jsx).
export function marksReadOnOpen() {
    return getPrefs().markReadOnOpen !== false;
}

// Download the current preferences as a JSON file (Settings → Data).
export function exportPrefs() {
    const blob = new Blob([JSON.stringify(getPrefs(), null, 2)], { type: 'application/json' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = 'sengen-preferences.json';
    document.body.appendChild(a);
    a.click();
    a.remove();
    setTimeout(() => URL.revokeObjectURL(url), 0);
}
