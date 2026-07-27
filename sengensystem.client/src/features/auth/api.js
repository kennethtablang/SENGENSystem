const TOKEN_KEY = 'sengen.token';

export function getToken() {
    return localStorage.getItem(TOKEN_KEY);
}

export function setToken(token) {
    localStorage.setItem(TOKEN_KEY, token);
}

export function clearToken() {
    localStorage.removeItem(TOKEN_KEY);
}

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

export async function registerAccount(data) {
    const response = await fetch('/api/auth/register', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(data)
    });
    if (!response.ok) throw await parseError(response);
    return response.json();
}

export async function loginAccount(data) {
    const response = await fetch('/api/auth/login', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(data)
    });
    if (!response.ok) throw await parseError(response);
    return response.json();
}

export async function verifyTwoFactor(data) {
    const response = await fetch('/api/auth/2fa/verify', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(data)
    });
    if (!response.ok) throw await parseError(response);
    return response.json();
}

export async function resendTwoFactor(challengeToken) {
    const response = await fetch('/api/auth/2fa/resend', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ challengeToken })
    });
    if (!response.ok) throw await parseError(response);
    return response.json();
}

export async function forgotPassword(email) {
    const response = await fetch('/api/auth/forgot-password', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ email })
    });
    if (!response.ok) throw await parseError(response);
    return response.json();
}

export async function resetPassword(data) {
    const response = await fetch('/api/auth/reset-password', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(data)
    });
    if (!response.ok) throw await parseError(response);
    return response.json();
}

export async function confirmEmailChange(token) {
    const response = await fetch('/api/profile/email/confirm', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ token })
    });
    if (!response.ok) throw await parseError(response);
    return response.json();
}

export async function fetchCurrentUser() {
    const token = getToken();
    if (!token) return null;
    const response = await fetch('/api/auth/me', {
        headers: { Authorization: `Bearer ${token}` }
    });
    if (!response.ok) return null;
    return response.json();
}
