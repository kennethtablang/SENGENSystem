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

// ---- Student (FR-ENL-01/02/04) ----

export function browseSections() {
    return authRequest('/api/enlistment/sections');
}

export function requestSlot(sectionId) {
    return authRequest('/api/enlistment/requests', { method: 'POST', body: { sectionId } });
}

export function myEnlistment() {
    return authRequest('/api/enlistment/mine');
}

export function cancelRequest(requestId) {
    return authRequest(`/api/enlistment/requests/${requestId}`, { method: 'DELETE' });
}

// ---- Registrar approvals (FR-ENL-04) ----

export function listApprovals({ status, search } = {}) {
    const params = new URLSearchParams();
    if (status && status !== 'All') params.set('status', status);
    if (search) params.set('search', search);
    const qs = params.toString() ? `?${params}` : '';
    return authRequest(`/api/enlistment/approvals${qs}`);
}

export function approveRequest(requestId) {
    return authRequest(`/api/enlistment/approvals/${requestId}/approve`, { method: 'POST' });
}

export function rejectRequest(requestId, reason) {
    return authRequest(`/api/enlistment/approvals/${requestId}/reject`, {
        method: 'POST',
        body: { reason: reason || null }
    });
}
