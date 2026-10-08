import { apiFetch } from '../shell/apiClient';

/* This module's error handling used to be the exception — it was the only one that recognised a
   401, and the only one that read ProblemDetails' `detail` and `reference`. Rather than keep the
   good version here and the poor one in twenty other files, that shape moved into the shared
   client (including the generation flow's `requiresConfirmation` counts) and every module now gets
   it. Nothing this module could do before it can do less of now. */
const authRequest = apiFetch;

/**
 * FR-SCHED-06: Academic Head triggers CSP generation for a semester.
 *
 * Generation replaces the semester's whole timetable. When part of it is already published the
 * server refuses with a 409 carrying `requiresConfirmation` and the counts at stake; pass
 * `replacePublished: true` to go ahead, which is the caller saying yes to discarding it.
 */
// `seed` reproduces a past arrangement (FR-SCHED-08): the same seed over the same inputs gives
// the same timetable. Omit it for a fresh arrangement.
export function generateSchedule(semesterId, { replacePublished = false, seed = null } = {}) {
    return authRequest('/api/scheduling/generate', {
        method: 'POST',
        body: { semesterId: semesterId ?? null, replacePublished, seed }
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
