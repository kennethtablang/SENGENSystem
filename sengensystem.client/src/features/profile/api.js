import { apiFetch } from '../shell/apiClient';

/* Every call goes through the shared client — see academic/api.js for the reasoning. */
const authPut = (url, data) => apiFetch(url, { method: 'PUT', body: data });
const authPost = (url, data) => apiFetch(url, { method: 'POST', body: data ?? {} });

export function updateProfile(data) {
    return authPut('/api/profile', data);
}

export function changePassword(data) {
    return authPut('/api/profile/password', data);
}

export function requestEmailChange(data) {
    return authPost('/api/profile/email/request', data);
}

// Two-factor authentication (opt-in email one-time code).
export function startTwoFactor() {
    return authPost('/api/profile/2fa/start');
}

export function enableTwoFactor(code) {
    return authPost('/api/profile/2fa/enable', { code });
}

export function disableTwoFactor(password) {
    return authPost('/api/profile/2fa/disable', { password });
}
