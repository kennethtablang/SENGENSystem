import { getToken } from '../auth/api';

async function parseError(response) {
    let payload = null;
    try {
        payload = await response.json();
    } catch {
        // non-JSON error body
    }
    // 401/403 come back with an empty body, so there is no payload to read a message from —
    // name them explicitly instead of falling through to the generic "Something went wrong".
    const authMessage =
        response.status === 401 ? 'Your session has expired — sign in again and retry.'
        : response.status === 403 ? 'You do not have permission to generate schedules.'
        : null;
    return {
        status: response.status,
        message: payload?.message || payload?.title || authMessage || 'Something went wrong. Please try again.',
        // ProblemDetails (e.g. an unexpected 500) carries its lead in `detail`; the 422 and
        // validation paths carry row-by-row reasons. Prefer explicit reasons, but fall back to
        // `detail` so a crash still shows the exception summary and trace reference rather than
        // just a bare title.
        reasons: payload?.reasons?.length ? payload.reasons : (payload?.detail ? [payload.detail] : []),
        reference: payload?.reference || null,
        // A refusal the caller can answer rather than only report: generating over a published
        // timetable comes back as a 409 asking to be confirmed, with the counts to confirm against.
        requiresConfirmation: payload?.requiresConfirmation === true,
        publishedCount: payload?.publishedCount ?? 0,
        publishedSections: payload?.publishedSections ?? 0,
        affectedStudents: payload?.affectedStudents ?? 0,
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

/**
 * FR-SCHED-06: Academic Head triggers CSP generation for a semester.
 *
 * Generation replaces the semester's whole timetable. When part of it is already published the
 * server refuses with a 409 carrying `requiresConfirmation` and the counts at stake; pass
 * `replacePublished: true` to go ahead, which is the caller saying yes to discarding it.
 */
export function generateSchedule(semesterId, { replacePublished = false } = {}) {
    return authRequest('/api/scheduling/generate', {
        method: 'POST',
        body: { semesterId: semesterId ?? null, replacePublished }
    });
}

// FR-SCHED-06: staff review of the current (draft or published) schedule.
export function getSchedule(semesterId) {
    const query = semesterId ? `?semesterId=${encodeURIComponent(semesterId)}` : '';
    return authRequest(`/api/scheduling/schedule${query}`);
}

// FR-SCHED-03/-08: the soft-constraint inputs the engine optimises against (faculty preferred
// windows + the load allocation), shown as the basis for a generated schedule.
export function getSoftConstraints(semesterId) {
    const query = semesterId ? `?semesterId=${encodeURIComponent(semesterId)}` : '';
    return authRequest(`/api/scheduling/soft-constraints${query}`);
}

// FR-SCHED-05: the Academic Head tunes the soft-constraint weights the engine optimises against.
export function updateSoftConstraintWeights(weights) {
    return authRequest('/api/scheduling/soft-constraints/weights', {
        method: 'PUT',
        body: weights
    });
}

// FR-SCHED-06: Academic Head signs off the draft as final & ready to publish (locks it),
// or reopens it for further generate/board edits.
export function finalizeSchedule(semesterId) {
    return authRequest(`/api/scheduling/${encodeURIComponent(semesterId)}/finalize`, { method: 'POST' });
}

export function reopenSchedule(semesterId) {
    return authRequest(`/api/scheduling/${encodeURIComponent(semesterId)}/reopen`, { method: 'POST' });
}

// FR-FAC-05: the signed-in user's own weekly timetable for the active semester.
export function getMySchedule(semesterId) {
    const query = semesterId ? `?semesterId=${encodeURIComponent(semesterId)}` : '';
    return authRequest(`/api/scheduling/my-schedule${query}`);
}
