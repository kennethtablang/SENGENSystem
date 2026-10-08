/* Where the bearer token lives.

   Split out of `auth/api.js` to break a genuine import cycle: `apiClient` needs the token to build
   the Authorization header, and `auth/api` needs `apiClient` to make its own calls. Modules can
   tolerate a cycle, but one at the very bottom of the dependency graph — every network call in the
   app passes through it — is not somewhere to rely on that.

   `auth/api.js` re-exports these three so the seven modules already importing them from there keep
   working; new code should import from here.

   Storage is `localStorage`, which remains an open finding (P1, "JWT in localStorage"): readable by
   any injected script, so an XSS bug becomes full token theft. Confining it to this one file is a
   precondition for changing that decision — an httpOnly cookie or short-lived access token plus
   refresh would now be a change to this module rather than to twenty-two. */

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
