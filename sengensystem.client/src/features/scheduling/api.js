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
        // 422 from the generator carries row-by-row reasons why no schedule was possible.
        reasons: payload?.reasons || [],
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

// FR-SCHED-06: Academic Head triggers CSP generation for a semester.
export function generateSchedule(semesterId) {
    return authRequest('/api/scheduling/generate', {
        method: 'POST',
        body: { semesterId: semesterId ?? null }
    });
}

// FR-SCHED-06: staff review of the current (draft or published) schedule.
export function getSchedule(semesterId) {
    const query = semesterId ? `?semesterId=${encodeURIComponent(semesterId)}` : '';
    return authRequest(`/api/scheduling/schedule${query}`);
}

// FR-FAC-05: the signed-in user's own weekly timetable for the active semester.
export function getMySchedule(semesterId) {
    const query = semesterId ? `?semesterId=${encodeURIComponent(semesterId)}` : '';
    return authRequest(`/api/scheduling/my-schedule${query}`);
}
