import { apiFetch } from '../shell/apiClient';
import { pageParams } from '../shell/useServerTable';

// The email outbox: what the system has sent, what is queued, and what gave up.

export function listOutbox({ status, ...page } = {}) {
    const params = pageParams(page);
    if (status && status !== 'All') params.set('status', status);
    const qs = params.toString() ? `?${params}` : '';
    return apiFetch(`/api/admin/outbox${qs}`);
}

/** Puts one permanently-failed email back in the queue. Refused for anything already sent. */
export function retryOutboxEmail(id) {
    return apiFetch(`/api/admin/outbox/${id}/retry`, { method: 'POST' });
}

/** The usual case after a mail outage: hundreds failed at once. */
export function retryAllFailed() {
    return apiFetch('/api/admin/outbox/retry-failed', { method: 'POST' });
}
