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
        fieldErrors: payload?.errors || {}
    };
}

function authHeaders() {
    return { Authorization: `Bearer ${getToken()}` };
}

// ---- Public (no account) ----

// FR-SIS-01: a new student / transferee self-submits the digital SIS.
export async function registerStudent(data) {
    const response = await fetch('/api/registration', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(data)
    });
    if (!response.ok) throw await parseError(response);
    return response.json();
}

// Step one: identify the returning student (student number + last name) and get back the year
// level and term they are about to activate into, so they check before anything is filed.
export async function lookupTermActivation(data) {
    const response = await fetch('/api/registration/term-activation/lookup', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(data)
    });
    if (!response.ok) throw await parseError(response);
    return response.json();
}

// Step two: finalize. Carries the confirmed year level and the term id from the lookup — the
// server refuses the request if that term is no longer the active one.
export async function requestTermActivation(data) {
    const response = await fetch('/api/registration/term-activation', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(data)
    });
    if (!response.ok) throw await parseError(response);
    return response.json();
}

// ---- Term activation control (Registrar / Academic Head / admins) ----

// The institution-wide switch for the public self-service activation form.
export async function getTermActivationControl() {
    const response = await fetch('/api/registration/term-activation/control', { headers: authHeaders() });
    if (!response.ok) throw await parseError(response);
    return response.json();
}

export async function setTermActivationControl(open) {
    const response = await fetch('/api/registration/term-activation/control', {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json', ...authHeaders() },
        body: JSON.stringify({ open })
    });
    if (!response.ok) throw await parseError(response);
    return response.json();
}

// ---- Admission Officer ----

export async function listTermActivations({ status, ...page } = {}) {
    const params = pageParams(page);
    if (status) params.set('status', status);
    const qs = params.toString() ? `?${params}` : '';
    const response = await fetch(`/api/registration/term-activation${qs}`, { headers: authHeaders() });
    if (!response.ok) throw await parseError(response);
    return response.json();
}

export async function validateTermActivation(id, data) {
    const response = await fetch(`/api/registration/term-activation/${id}/validate`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', ...authHeaders() },
        body: JSON.stringify(data)
    });
    if (!response.ok) throw await parseError(response);
    return response.json();
}

// Admission Officer records the official student number (issued by a separate system).
// `status` chooses the view: 'pending' (still to number, the default), 'numbered', or 'all'.
export async function listAssignableRegistrations({ status, ...page } = {}) {
    const params = pageParams(page);
    if (status) params.set('status', status);
    const qs = params.toString() ? `?${params}` : '';
    const response = await fetch(`/api/registration/student-number${qs}`, { headers: authHeaders() });
    if (!response.ok) throw await parseError(response);
    return response.json();
}

export async function assignStudentNumber(id, studentNumber) {
    const response = await fetch(`/api/registration/${id}/student-number`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', ...authHeaders() },
        body: JSON.stringify({ studentNumber })
    });
    if (!response.ok) throw await parseError(response);
    return response.json();
}

// ---- Registrar ----

/* Paged, filtered, and sorted by the server (see useServerTable). Spread the hook's `query` in:
   listRegistrations({ status, ...table.query }). */
export async function listRegistrations({ status, ...page } = {}) {
    const params = pageParams(page);
    if (status && status !== 'All') params.set('status', status);
    const qs = params.toString() ? `?${params}` : '';
    const response = await fetch(`/api/registration${qs}`, { headers: authHeaders() });
    if (!response.ok) throw await parseError(response);
    return response.json();
}

export async function getRegistration(id) {
    const response = await fetch(`/api/registration/${id}`, { headers: authHeaders() });
    if (!response.ok) throw await parseError(response);
    return response.json();
}

export async function updateRegistration(id, data) {
    const response = await fetch(`/api/registration/${id}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json', ...authHeaders() },
        body: JSON.stringify(data)
    });
    if (!response.ok) throw await parseError(response);
    return response.json();
}
