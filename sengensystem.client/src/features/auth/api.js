import { apiFetch } from '../shell/apiClient';
import { getToken } from '../shell/token';

/* Token storage moved to `shell/token.js` to break the import cycle with the shared client — see
   that file. Re-exported here because seven modules already import these from this path. */
export { getToken, setToken, clearToken } from '../shell/token';

/* Every call below passes `auth: false`. These are the endpoints you use *to get* a token, so
   sending one would be meaningless — and, more importantly, the shared client's global 401 handling
   is deliberately switched off for `/api/auth/*`: login answers a wrong password with a 401, and
   redirecting on that would replace "Invalid email or password" with a page reload, leaving the
   user unable to discover they simply mistyped it. */

export function registerAccount(data) {
    return apiFetch('/api/auth/register', { method: 'POST', body: data, auth: false });
}

export function loginAccount(data) {
    return apiFetch('/api/auth/login', { method: 'POST', body: data, auth: false });
}

export function verifyTwoFactor(data) {
    return apiFetch('/api/auth/2fa/verify', { method: 'POST', body: data, auth: false });
}

export function resendTwoFactor(challengeToken) {
    return apiFetch('/api/auth/2fa/resend', {
        method: 'POST', body: { challengeToken }, auth: false
    });
}

export function forgotPassword(email) {
    return apiFetch('/api/auth/forgot-password', { method: 'POST', body: { email }, auth: false });
}

export function resetPassword(data) {
    return apiFetch('/api/auth/reset-password', { method: 'POST', body: data, auth: false });
}

export function confirmEmailChange(token) {
    return apiFetch('/api/profile/email/confirm', { method: 'POST', body: { token }, auth: false });
}

/**
 * Resolves the signed-in user, or null.
 *
 * Deliberately swallows every failure rather than throwing: this runs on app start to decide
 * whether there is a session at all, and a 401 here is the ordinary "not signed in" answer, not an
 * error. It bypasses the shared client for that reason — `apiFetch` would clear the token and
 * redirect to login, which is exactly right everywhere else and exactly wrong on the call whose
 * job is to find out whether we are logged in.
 */
export async function fetchCurrentUser() {
    const token = getToken();
    if (!token) return null;
    const response = await fetch('/api/auth/me', {
        headers: { Authorization: `Bearer ${token}` }
    });
    if (!response.ok) return null;
    return response.json();
}
