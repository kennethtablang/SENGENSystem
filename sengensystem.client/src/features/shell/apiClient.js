import { getToken, clearToken } from './token';

/* One fetch wrapper for every API call the client makes.

   Before this, `parseError` was copy-pasted into 21 `api.js` modules, each with its own fetch and
   Authorization wiring. Three things followed from that, and this file exists to fix all three:

   1. **An expired token surfaced as nonsense.** Only `scheduling/api.js` recognised a 401. Everywhere
      else a session that had quietly lapsed produced whatever generic message that module happened
      to write — "Something went wrong. Please try again." — on a page that would never work again
      until the user thought to reload. Now a 401 clears the token and sends them to sign in.

   2. **ProblemDetails was read by exactly one module.** The server's global 500 handler returns
      `detail` and `reference` (a trace id someone can quote), and 20 of the 21 modules threw both
      away and showed the bare title. They are folded in here, so every page gets the lead.

   3. **Twenty-one places to change one decision.** Auth, error shape, and base URL now live once. */

const AUTH_ENDPOINT = /^\/api\/auth\//;

/* Where an expired session lands. Kept as a full assignment rather than a router navigate because
   this module is imported by plain functions with no access to router context, and a hard
   navigation is also the more honest recovery: whatever half-initialised state the page was in is
   discarded along with the token. */
function redirectToLogin() {
    // Preserve where they were, so signing back in can return them rather than dumping them on the
    // dashboard having lost their place.
    const from = window.location.pathname + window.location.search;
    const next = from && from !== '/login' ? `?next=${encodeURIComponent(from)}` : '';
    window.location.assign(`/login${next}`);
}

/**
 * Turns a failed response into the error object every page in this app expects to catch.
 *
 * The shape is the union of what the modules used to build individually — `reasons` and
 * `fieldErrors` were common, `detail`/`reference` were scheduling-only, and the generation flow's
 * confirmation fields were unique to it. Keeping them all here costs nothing (absent keys are
 * simply null or empty) and means a page can start reading one without its api module changing.
 */
export async function parseError(response) {
    let payload = null;
    try {
        payload = await response.json();
    } catch {
        // Non-JSON error body — a proxy error page, or an empty 401/403.
    }

    // 401/403 often come back with no body at all, so there is nothing to read a message from.
    // Naming them beats falling through to the generic line.
    const authMessage =
        response.status === 401 ? 'Your session has expired — sign in again and retry.'
        : response.status === 403 ? 'You do not have permission to do that.'
        : response.status === 429 ? 'Too many attempts. Wait a moment and try again.'
        : null;

    return {
        status: response.status,
        message: payload?.message || payload?.title || authMessage || 'Something went wrong. Please try again.',
        /* ProblemDetails carries its lead in `detail`; the 422 and validation paths carry row-by-row
           `reasons`. Prefer explicit reasons, but fall back to `detail` so an unexpected 500 still
           shows the exception summary rather than just a bare title. */
        reasons: payload?.reasons?.length ? payload.reasons : (payload?.detail ? [payload.detail] : []),
        reference: payload?.reference || null,
        fieldErrors: payload?.errors || {},
        /* Refusals the caller can answer rather than only report — schedule generation over a
           published timetable comes back as a 409 asking to be confirmed, with the counts at stake. */
        requiresConfirmation: payload?.requiresConfirmation === true,
        publishedCount: payload?.publishedCount ?? 0,
        publishedSections: payload?.publishedSections ?? 0,
        affectedStudents: payload?.affectedStudents ?? 0
    };
}

/**
 * The one call every api module goes through.
 *
 * @param {string} url
 * @param {object} [options]
 * @param {string} [options.method]
 * @param {any}    [options.body]         JSON-serialised unless it is FormData, which is sent as-is.
 * @param {boolean}[options.auth]         Send the bearer token. Default true.
 * @param {boolean}[options.raw]          Return the Response instead of parsed JSON (for blobs).
 */
export async function apiFetch(url, { method = 'GET', body, auth = true, raw = false, headers } = {}) {
    const isForm = typeof FormData !== 'undefined' && body instanceof FormData;

    const response = await fetch(url, {
        method,
        headers: {
            // FormData must set its own multipart boundary — declaring JSON here would corrupt an
            // upload in a way that only shows up server-side as a malformed request.
            ...(body && !isForm ? { 'Content-Type': 'application/json' } : {}),
            ...(auth ? { Authorization: `Bearer ${getToken()}` } : {}),
            ...(headers || {})
        },
        ...(body ? { body: isForm ? body : JSON.stringify(body) } : {})
    });

    if (!response.ok) {
        /* A 401 from a signed-in call means the token is gone or expired, and every subsequent call
           on this page will fail the same way. Clear it and bounce to sign-in once.

           Deliberately NOT applied to /api/auth/* — the login endpoint answers a wrong password with
           a 401, and redirecting on that would replace "Invalid email or password" with a page
           reload, leaving the user unable to discover they simply mistyped it. */
        if (response.status === 401 && auth && !AUTH_ENDPOINT.test(url)) {
            clearToken();
            const error = await parseError(response);
            redirectToLogin();
            throw error;
        }
        throw await parseError(response);
    }

    if (raw) return response;
    // 204, and any other body-less success, must not be handed to response.json().
    if (response.status === 204 || response.headers.get('content-length') === '0') return null;
    return response.json();
}

/**
 * Downloads a bearer-authenticated file and hands it to the browser.
 *
 * A plain `<a href>` cannot carry the Authorization header, which is why every report and export in
 * this app goes through a blob. Centralised here so the 401 handling above applies to downloads
 * too — previously a download on an expired session produced a corrupt file rather than an error.
 */
export async function apiDownload(url, filename) {
    const response = await apiFetch(url, { raw: true });
    const blob = await response.blob();
    const href = URL.createObjectURL(blob);
    const anchor = document.createElement('a');
    anchor.href = href;
    anchor.download = filename;
    document.body.appendChild(anchor);
    anchor.click();
    anchor.remove();
    setTimeout(() => URL.revokeObjectURL(href), 0);
}
