import { getToken } from '../auth/api';

// ISO/IEC 25010 rating survey. Takers reach the instrument two ways — the anonymous emailed link
// (token in the URL) or signed in from the bell notice ("mine"). The admin endpoints (audience,
// dispatch, collection window, results) require a Super Admin session.

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

async function json(response) {
    if (!response.ok) throw await parseError(response);
    return response.json();
}

// ---- Taker: emailed link (no auth) ----

export async function getSurvey(token) {
    return json(await fetch(`/api/survey/${encodeURIComponent(token)}`));
}

export async function submitSurvey(token, data) {
    return json(await fetch(`/api/survey/${encodeURIComponent(token)}`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(data)
    }));
}

// ---- Taker: signed in, opened from the bell notice ----

export async function getMySurvey() {
    return json(await fetch('/api/survey/mine', { headers: authHeaders() }));
}

export async function submitMySurvey(data) {
    return json(await fetch('/api/survey/mine', {
        method: 'POST',
        headers: authHeaders(true),
        body: JSON.stringify(data)
    }));
}

// ---- Super Admin: choosing who participates ----

/** Every active account with its current invite status, for the recipients picker. */
export async function getAudience() {
    return json(await fetch('/api/admin/survey/audience', { headers: authHeaders() }));
}

export async function listInvitations() {
    return json(await fetch('/api/admin/survey/invitations', { headers: authHeaders() }));
}

/** Sends to explicitly picked users (and optionally whole roles), pushing a bell notice and/or email. */
export async function sendInvitations({ userIds = [], roles = [], note = '', pushNotification = true, sendEmail = true } = {}) {
    return json(await fetch('/api/admin/survey/invitations', {
        method: 'POST',
        headers: authHeaders(true),
        body: JSON.stringify({ userIds, roles, note, pushNotification, sendEmail })
    }));
}

/** Nudges people who were invited but haven't answered. Empty ids = every pending invitation. */
export async function remindInvitations({ invitationIds = [], note = '', pushNotification = true, sendEmail = false } = {}) {
    return json(await fetch('/api/admin/survey/invitations/remind', {
        method: 'POST',
        headers: authHeaders(true),
        body: JSON.stringify({ invitationIds, note, pushNotification, sendEmail })
    }));
}

export async function withdrawInvitation(id) {
    return json(await fetch(`/api/admin/survey/invitations/${encodeURIComponent(id)}`, {
        method: 'DELETE',
        headers: authHeaders()
    }));
}

// ---- Super Admin: collection window + results ----

export async function getCollection() {
    return json(await fetch('/api/admin/survey/collection', { headers: authHeaders() }));
}

/** Opens/closes collection and sets the response goal. */
export async function setCollection({ isOpen, targetResponses } = {}) {
    return json(await fetch('/api/admin/survey/collection', {
        method: 'POST',
        headers: authHeaders(true),
        body: JSON.stringify({ isOpen, targetResponses })
    }));
}

export async function getResults() {
    return json(await fetch('/api/admin/survey/results', { headers: authHeaders() }));
}

/** Streams the raw responses as CSV and triggers a browser download. */
export async function exportResults() {
    const response = await fetch('/api/admin/survey/results/export', { headers: authHeaders() });
    if (!response.ok) throw await parseError(response);
    const blob = await response.blob();
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `sengen-survey-results-${new Date().toISOString().slice(0, 10)}.csv`;
    document.body.appendChild(a);
    a.click();
    a.remove();
    URL.revokeObjectURL(url);
}
