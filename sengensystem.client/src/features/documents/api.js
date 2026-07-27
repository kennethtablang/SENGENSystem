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

// ---- Staff (Admission Officer / Registrar): checklist board (FR-DOC-01..03) ----

export function listChecklists({ completion, search } = {}) {
    const params = new URLSearchParams();
    if (completion && completion !== 'All') params.set('completion', completion);
    if (search) params.set('search', search);
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
