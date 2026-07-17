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

// Returning student requests activation for the active term (student number + last name).
export async function requestTermActivation(data) {
    const response = await fetch('/api/registration/term-activation', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(data)
    });
    if (!response.ok) throw await parseError(response);
    return response.json();
}

// ---- Admission Officer ----

export async function listTermActivations(status) {
    const qs = status ? `?status=${encodeURIComponent(status)}` : '';
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

// ---- Registrar ----

export async function listRegistrations({ status, search } = {}) {
    const params = new URLSearchParams();
    if (status && status !== 'All') params.set('status', status);
    if (search) params.set('search', search);
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
