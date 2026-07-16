import { getToken } from '../auth/api';

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
export async function getAuditTrail() {
    const response = await fetch('/api/audit', {
        headers: { Authorization: `Bearer ${getToken()}` }
    });
    if (!response.ok) throw await parseError(response);
    return response.json();
}
