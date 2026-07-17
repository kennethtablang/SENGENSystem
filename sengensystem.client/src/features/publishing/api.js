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

async function authRequest(url, { method = 'GET', body } = {}) {
    const response = await fetch(url, {
        method,
        headers: {
            ...(body ? { 'Content-Type': 'application/json' } : {}),
            Authorization: `Bearer ${getToken()}`
        },
        ...(body ? { body: JSON.stringify(body) } : {})
    });
    if (!response.ok) throw await parseError(response);
    return response.json();
}

// Full (draft + published) schedule for the active semester — powers the pre-publish review.
export function getFullSchedule(semesterId) {
    const query = semesterId ? `?semesterId=${encodeURIComponent(semesterId)}` : '';
    return authRequest(`/api/scheduling/schedule${query}`);
}

// FR-PUB-01: the Registrar publishes the semester's finalized schedule.
export function publishSchedule(semesterId) {
    return authRequest(`/api/publishing/${encodeURIComponent(semesterId)}/publish`, { method: 'POST' });
}

// FR-PUB-02: published-only view, filterable by day and class block.
export function getPublishedSchedule({ semesterId, day, cohort } = {}) {
    const qs = new URLSearchParams();
    if (semesterId) qs.set('semesterId', semesterId);
    if (day) qs.set('day', day);
    if (cohort) qs.set('cohort', cohort);
    const query = qs.toString();
    return authRequest(`/api/publishing/schedule${query ? `?${query}` : ''}`);
}
