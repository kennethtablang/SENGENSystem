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
        message: payload?.message || payload?.title || 'Something went wrong. Please try again.',
        reasons: payload?.reasons || [],
        fieldErrors: payload?.errors || {}
    };
}

async function authRequest(url, { method = 'GET', body } = {}) {
    const response = await fetch(url, {
        method,
        headers: {
            ...(body ? { 'Content-Type': 'application/json' } : {}),
            Authorization: `Bearer ${getToken()}`
        },
        ...(body ? { body: JSON.stringify(body) } : {})
    });
    if (!response.ok) throw await parseError(response);
    return response.json();
}

// FR-PRE-02/04: Admission Officer pre-authorization for online slot selection.

export function listPreAuthorizations({ filter, ...page } = {}) {
    const params = pageParams(page);
    if (filter && filter !== 'All') params.set('filter', filter);
    const qs = params.toString() ? `?${params}` : '';
    return authRequest(`/api/pre-authorization${qs}`);
}

export function grantPreAuthorization(registrationId) {
    return authRequest(`/api/pre-authorization/${registrationId}`, { method: 'POST' });
}

export function revokePreAuthorization(registrationId) {
    return authRequest(`/api/pre-authorization/${registrationId}`, { method: 'DELETE' });
}
