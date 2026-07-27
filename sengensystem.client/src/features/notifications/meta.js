// Presentation metadata shared by the bell dropdown and the Notifications page.

export const kindIcons = {
    SchedulePublished: 'send',
    EnlistmentApproved: 'check',
    EnlistmentRejected: 'listcheck',
    SlotRequested: 'listcheck',
    FacultyLoadUpdated: 'users',
    Documents: 'file',
    Account: 'user',
    Registration: 'idcard',
    TermActivation: 'check',
    SectionFull: 'users',
    Survey: 'star',
    General: 'bell'
};

export function iconFor(kind) {
    return kindIcons[kind] ?? 'bell';
}

export function relativeTime(iso) {
    const then = new Date(iso).getTime();
    const minutes = Math.floor((Date.now() - then) / 60000);
    if (minutes < 1) return 'just now';
    if (minutes < 60) return `${minutes}m ago`;
    const hours = Math.floor(minutes / 60);
    if (hours < 24) return `${hours}h ago`;
    const days = Math.floor(hours / 24);
    if (days < 7) return `${days}d ago`;
    return new Date(iso).toLocaleDateString(undefined, { month: 'short', day: 'numeric' });
}

// Lets the bell and the page stay in sync after either marks something read.
export const NOTIFICATIONS_CHANGED = 'sengen:notifications';

export function announceNotificationsChanged() {
    window.dispatchEvent(new Event(NOTIFICATIONS_CHANGED));
}
