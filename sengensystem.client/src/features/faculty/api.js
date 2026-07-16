import { getToken } from '../auth/api';

// Faculty load management (Academic Head): allocate subjects to faculty per semester.

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

async function get(url) {
    const response = await fetch(url, { headers: authHeaders() });
    if (!response.ok) throw await parseError(response);
    return response.json();
}

export const listFacultyLoad = (semesterId) =>
    get(`/api/faculty-load${semesterId ? `?semesterId=${semesterId}` : ''}`);

export const getFacultySubjects = (facultyProfileId, semesterId) =>
    get(`/api/faculty-load/${facultyProfileId}/subjects${semesterId ? `?semesterId=${semesterId}` : ''}`);

// FR-SCHED-03: preferred teaching windows, consumed by the CSP engine's soft scoring.
export const getFacultyPreferences = (facultyProfileId) =>
    get(`/api/faculty-load/${facultyProfileId}/preferences`);

// windows: [{ day, startMinutes, endMinutes }]
export async function saveFacultyPreferences(facultyProfileId, windows) {
    const response = await fetch(`/api/faculty-load/${facultyProfileId}/preferences`, {
        method: 'PUT',
        headers: authHeaders(true),
        body: JSON.stringify({ windows })
    });
    if (!response.ok) throw await parseError(response);
    return response.json();
}

// items: [{ subjectId, classSectionId }]
export async function saveFacultyLoad(facultyProfileId, semesterId, items) {
    const response = await fetch(`/api/faculty-load/${facultyProfileId}`, {
        method: 'PUT',
        headers: authHeaders(true),
        body: JSON.stringify({ semesterId, items })
    });
    if (!response.ok) throw await parseError(response);
    return response.json();
}
