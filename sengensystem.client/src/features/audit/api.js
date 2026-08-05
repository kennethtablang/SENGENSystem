import { getToken } from '../auth/api';
import { pageParams } from '../shell/useServerTable';

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

// FR-AUD-01: the School Admin reads the accountability log (newest first).
export async function getAuditTrail({ action, ...page } = {}) {
    const params = pageParams(page);
    if (action && action !== 'All') params.set('action', action);
    const qs = params.toString() ? `?${params}` : '';
    const response = await fetch(`/api/audit${qs}`, {
        headers: { Authorization: `Bearer ${getToken()}` }
    });
    if (!response.ok) throw await parseError(response);
    return response.json();
}
