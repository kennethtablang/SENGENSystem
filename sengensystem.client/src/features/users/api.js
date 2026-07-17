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

function authHeaders(json) {
    const h = { Authorization: `Bearer ${getToken()}` };
    if (json) h['Content-Type'] = 'application/json';
    return h;
}

// FR-AUTH-07: School Admin account management.
export async function listUsers({ role, status, search } = {}) {
    const params = new URLSearchParams();
    if (role && role !== 'All') params.set('role', role);
    if (status && status !== 'All') params.set('status', status);
    if (search) params.set('search', search);
    const qs = params.toString() ? `?${params}` : '';
    const response = await fetch(`/api/users${qs}`, { headers: authHeaders() });
    if (!response.ok) throw await parseError(response);
    return response.json();
}

export async function createUser(data) {
    const response = await fetch('/api/users', {
        method: 'POST', headers: authHeaders(true), body: JSON.stringify(data)
    });
    if (!response.ok) throw await parseError(response);
    return response.json();
}

export async function updateUser(id, data) {
    const response = await fetch(`/api/users/${id}`, {
        method: 'PUT', headers: authHeaders(true), body: JSON.stringify(data)
    });
    if (!response.ok) throw await parseError(response);
    return response.json();
}

export async function setUserActive(id, isActive) {
    const response = await fetch(`/api/users/${id}/active`, {
        method: 'POST', headers: authHeaders(true), body: JSON.stringify({ isActive })
    });
    if (!response.ok) throw await parseError(response);
    return response.json();
}

export async function resetUserPassword(id, newPassword) {
    const response = await fetch(`/api/users/${id}/password`, {
        method: 'POST', headers: authHeaders(true), body: JSON.stringify({ newPassword })
    });
    if (!response.ok) throw await parseError(response);
    return response.json();
}
