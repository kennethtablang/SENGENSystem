import { apiFetch, apiDownload } from '../shell/apiClient';

// ISO/IEC 25010 rating survey. Takers reach the instrument two ways — the anonymous emailed link
// (token in the URL) or signed in from the bell notice ("mine"). The admin endpoints (audience,
// dispatch, collection window, results) require a Super Admin session.

/* Every call goes through the shared client — see academic/api.js for the reasoning. The two
   token-addressed endpoints pass `auth: false`: the emailed link is deliberately usable by a
   respondent who has no account, and the token in the URL is the whole credential. */

// ---- Taker: emailed link (no auth) ----

export function getSurvey(token) {
    return apiFetch(`/api/survey/${encodeURIComponent(token)}`, { auth: false });
}

export function submitSurvey(token, data) {
    return apiFetch(`/api/survey/${encodeURIComponent(token)}`, {
        method: 'POST', body: data, auth: false
    });
}

// ---- Taker: signed in, opened from the bell notice ----

export function getMySurvey() {
    return apiFetch('/api/survey/mine');
}

export function submitMySurvey(data) {
    return apiFetch('/api/survey/mine', { method: 'POST', body: data });
}

// ---- Super Admin: choosing who participates ----

/** Every active account with its current invite status, for the recipients picker. */
export function getAudience() {
    return apiFetch('/api/admin/survey/audience');
}

export function listInvitations() {
    return apiFetch('/api/admin/survey/invitations');
}

/** Sends to explicitly picked users (and optionally whole roles), pushing a bell notice and/or email. */
export function sendInvitations({ userIds = [], roles = [], note = '', pushNotification = true, sendEmail = true } = {}) {
    return apiFetch('/api/admin/survey/invitations', {
        method: 'POST',
        body: { userIds, roles, note, pushNotification, sendEmail }
    });
}

/** Nudges people who were invited but haven't answered. Empty ids = every pending invitation. */
export function remindInvitations({ invitationIds = [], note = '', pushNotification = true, sendEmail = false } = {}) {
    return apiFetch('/api/admin/survey/invitations/remind', {
        method: 'POST',
        body: { invitationIds, note, pushNotification, sendEmail }
    });
}

export function withdrawInvitation(id) {
    return apiFetch(`/api/admin/survey/invitations/${encodeURIComponent(id)}`, { method: 'DELETE' });
}

// ---- Super Admin: collection window + results ----

export function getCollection() {
    return apiFetch('/api/admin/survey/collection');
}

/** Opens/closes collection and sets the response goal. */
export function setCollection({ isOpen, targetResponses } = {}) {
    return apiFetch('/api/admin/survey/collection', {
        method: 'POST', body: { isOpen, targetResponses }
    });
}

export function getResults() {
    return apiFetch('/api/admin/survey/results');
}

/** Streams the raw responses as CSV and triggers a browser download. */
export function exportResults() {
    return apiDownload(
        '/api/admin/survey/results/export',
        `sengen-survey-results-${new Date().toISOString().slice(0, 10)}.csv`);
}
