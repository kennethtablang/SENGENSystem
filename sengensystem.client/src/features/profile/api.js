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

async function authPut(url, data) {
    const response = await fetch(url, {
        method: 'PUT',
        headers: {
            'Content-Type': 'application/json',
            Authorization: `Bearer ${getToken()}`
        },
        body: JSON.stringify(data)
    });
    if (!response.ok) throw await parseError(response);
    return response.json();
}

export function updateProfile(data) {
    return authPut('/api/profile', data);
}

export function changePassword(data) {
    return authPut('/api/profile/password', data);
}

async function authPost(url, data) {
    const response = await fetch(url, {
        method: 'POST',
        headers: {
            'Content-Type': 'application/json',
            Authorization: `Bearer ${getToken()}`
        },
        body: JSON.stringify(data ?? {})
    });
    if (!response.ok) throw await parseError(response);
    return response.json();
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
