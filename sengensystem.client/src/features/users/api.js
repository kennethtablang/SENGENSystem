import { apiFetch } from '../shell/apiClient';
import { pageParams } from '../shell/useServerTable';

/* Every call goes through the shared client — see academic/api.js for the reasoning. */

// FR-AUTH-07: School Admin account management.
export function listUsers({ role, status, ...page } = {}) {
    const params = pageParams(page);
    if (role && role !== 'All') params.set('role', role);
    if (status && status !== 'All') params.set('status', status);
    const qs = params.toString() ? `?${params}` : '';
    return apiFetch(`/api/users${qs}`);
}

export function createUser(data) {
    return apiFetch('/api/users', { method: 'POST', body: data });
}

export function updateUser(id, data) {
    return apiFetch(`/api/users/${id}`, { method: 'PUT', body: data });
}

export function setUserActive(id, isActive) {
    return apiFetch(`/api/users/${id}/active`, { method: 'POST', body: { isActive } });
}

export function resetUserPassword(id, newPassword) {
    return apiFetch(`/api/users/${id}/password`, { method: 'POST', body: { newPassword } });
}
