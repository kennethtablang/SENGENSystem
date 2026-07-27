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

/**
 * Published sections for the active term. By default the server narrows them to the subjects the
 * signed-in student's program and year level still owe this term (FR-ENL-01/06); pass
 * `{ all: true }` to see every published section instead. Browsing wider never widens what may be
 * requested — the request leg re-checks the same list.
 */
export function browseSections({ all } = {}) {
    return authRequest(`/api/enlistment/sections${all ? '?all=true' : ''}`);
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

/**
 * FR-ENL-08: decide many requests at once. Pass `requestIds` for a checkbox selection, or
 * `allPending: true` to sweep the active term's queue — optionally narrowed to one student
 * (`studentRegistrationId`) or one section (`sectionId`). Every request still goes through the
 * same per-request checks; the response reports each outcome so skips can be shown with reasons.
 */
export function bulkApprove({ requestIds, allPending, studentRegistrationId, sectionId } = {}) {
    return authRequest('/api/enlistment/approvals/bulk-approve', {
        method: 'POST',
        body: {
            requestIds: requestIds ?? null,
            allPending: !!allPending,
            studentRegistrationId: studentRegistrationId ?? null,
            sectionId: sectionId ?? null
        }
    });
}

export function rejectRequest(requestId, reason) {
    return authRequest(`/api/enlistment/approvals/${requestId}/reject`, {
        method: 'POST',
        body: { reason: reason || null }
    });
}

// FR-ENL-03 manual override: raise a section's seat cap to complete a short section.
export function overrideCapacity(sectionId, capacity, reason) {
    return authRequest(`/api/enlistment/approvals/sections/${sectionId}/capacity`, {
        method: 'POST',
        body: { capacity, reason: reason || null }
    });
}
