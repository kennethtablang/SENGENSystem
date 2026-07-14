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

export async function fetchCurrentUser() {
    const token = getToken();
    if (!token) return null;
    const response = await fetch('/api/auth/me', {
        headers: { Authorization: `Bearer ${token}` }
    });
    if (!response.ok) return null;
    return response.json();
}
