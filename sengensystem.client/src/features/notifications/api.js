import { apiFetch } from '../shell/apiClient';

// In-app bell notifications: the signed-in user's notices (FR-NOTIF).

export function listNotifications({ take, unreadOnly } = {}) {
    const qs = new URLSearchParams();
    if (take) qs.set('take', take);
    if (unreadOnly) qs.set('unreadOnly', 'true');
    const s = qs.toString();
    return apiFetch(`/api/notifications${s ? `?${s}` : ''}`);
}

export function markRead(id) {
    return apiFetch(`/api/notifications/${id}/read`, { method: 'POST' });
}

export function markAllRead() {
    return apiFetch('/api/notifications/read-all', { method: 'POST' });
}
