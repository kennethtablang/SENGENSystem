import { apiFetch } from '../shell/apiClient';
import { pageParams } from '../shell/useServerTable';

/* Every call here goes through the shared client — one place for the auth header, the
   ProblemDetails error shape, and the global 401 handling that signs a lapsed session out
   rather than failing with a generic message on a page that will never work again. */
const authRequest = apiFetch;

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

/**
 * Withdraw from a section. One route for both cases because they are one thing to the student:
 * a still-pending request is cancelled, an approved one is *dropped* and its seat is returned to
 * the section. Dropping is only allowed while the term is in the enlistment stage — after that the
 * roster is the Registrar's to change.
 */
export function cancelRequest(requestId) {
    return authRequest(`/api/enlistment/requests/${requestId}`, { method: 'DELETE' });
}

// ---- Registrar approvals (FR-ENL-04) ----

export function listApprovals({ status, ...page } = {}) {
    const params = pageParams(page);
    if (status && status !== 'All') params.set('status', status);
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

/**
 * Release a seat that was already approved (FR-ENL-04). Rejection only applies to a request still
 * pending, so this is the only undo for a mis-approval — and the only action that gives a seat back
 * to a section's enrolled count.
 */
export function dropSeat(requestId, reason) {
    return authRequest(`/api/enlistment/approvals/${requestId}/drop`, {
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

// F-08 follow-up: sections whose stored seat count disagrees with their live approved requests.
export function seatCountMismatches() {
    return authRequest('/api/enlistment/seat-counts/mismatches');
}

// Correct one section's seat count to the number of live approved requests. Explicit, never
// automatic — a mismatch means something the seat lifecycle did not record.
export function reconcileSeatCount(sectionId) {
    return authRequest(`/api/enlistment/seat-counts/${sectionId}/reconcile`, { method: 'POST' });
}
