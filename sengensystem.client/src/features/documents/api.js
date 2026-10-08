import { apiFetch } from '../shell/apiClient';
import { pageParams } from '../shell/useServerTable';

/* Every call here goes through the shared client — one place for the auth header, the
   ProblemDetails error shape, and the global 401 handling that signs a lapsed session out
   rather than failing with a generic message on a page that will never work again. */
const authRequest = apiFetch;

// ---- Staff (Admission Officer / Registrar): checklist board (FR-DOC-01..03) ----

export function listChecklists({ completion, ...page } = {}) {
    const params = pageParams(page);
    if (completion && completion !== 'All') params.set('completion', completion);
    const qs = params.toString() ? `?${params}` : '';
    return authRequest(`/api/documents${qs}`);
}

export function updateDocumentStatus(documentId, status) {
    return authRequest(`/api/documents/${documentId}`, { method: 'PUT', body: { status } });
}

// FR-DOC-05: reminder emails; omit registrationId to sweep every incomplete checklist.
export function sendReminders(registrationId) {
    return authRequest('/api/documents/reminders', {
        method: 'POST',
        body: { registrationId: registrationId ?? null }
    });
}

// ---- Configurable requirement catalog (FR-DOC-01) ----

export function listRequirements() {
    return authRequest('/api/requirements');
}

export function createRequirement({ name, description, programs, isActive }) {
    return authRequest('/api/requirements', {
        method: 'POST',
        body: { name, description, programs, isActive }
    });
}

export function updateRequirement(id, { name, description, programs, isActive }) {
    return authRequest(`/api/requirements/${id}`, {
        method: 'PUT',
        body: { name, description, programs, isActive }
    });
}

export function archiveRequirement(id) {
    return authRequest(`/api/requirements/${id}`, { method: 'DELETE' });
}

// ---- Student: own record link + checklist (FR-ENL-05 identity link) ----

export function getMyLink() {
    return authRequest('/api/registration/link');
}

export function claimRecord(studentNumber, dateOfBirth) {
    return authRequest('/api/registration/link', { method: 'POST', body: { studentNumber, dateOfBirth } });
}
