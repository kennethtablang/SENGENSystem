import { apiFetch } from '../shell/apiClient';

/* Every call here goes through the shared client — one place for the auth header, the
   ProblemDetails error shape, and the global 401 handling that signs a lapsed session out rather
   than failing with a generic message on a page that will never work again. */
const authRequest = apiFetch;

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
