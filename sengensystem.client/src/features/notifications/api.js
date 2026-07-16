import { getToken } from '../auth/api';

// In-app bell notifications: the signed-in user's notices (FR-NOTIF).

async function parseError(response) {
    let payload = null;
    try {
        payload = await response.json();
    } catch {
        // non-JSON error body
    }
    return {
        status: response.status,
        message: payload?.message || payload?.title || 'Something went wrong. Please try again.'
    };
}

function authHeaders() {
    return { Authorization: `Bearer ${getToken()}` };
}

export async function listNotifications({ take, unreadOnly } = {}) {
    const qs = new URLSearchParams();
    if (take) qs.set('take', take);
    if (unreadOnly) qs.set('unreadOnly', 'true');
    const s = qs.toString();
    const response = await fetch(`/api/notifications${s ? `?${s}` : ''}`, { headers: authHeaders() });
    if (!response.ok) throw await parseError(response);
    return response.json();
}

export async function markRead(id) {
    const response = await fetch(`/api/notifications/${id}/read`, { method: 'POST', headers: authHeaders() });
    if (!response.ok) throw await parseError(response);
    return response.json();
}

export async function markAllRead() {
    const response = await fetch('/api/notifications/read-all', { method: 'POST', headers: authHeaders() });
    if (!response.ok) throw await parseError(response);
    return response.json();
}
