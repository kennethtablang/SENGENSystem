import { apiFetch } from '../shell/apiClient';

// Faculty load management (Academic Head): allocate subjects to faculty per semester.

/* Every call goes through the shared client — see academic/api.js for the reasoning. */
const get = (url) => apiFetch(url);

export const listFacultyLoad = (semesterId) =>
    get(`/api/faculty-load${semesterId ? `?semesterId=${semesterId}` : ''}`);

export const getFacultySubjects = (facultyProfileId, semesterId) =>
    get(`/api/faculty-load/${facultyProfileId}/subjects${semesterId ? `?semesterId=${semesterId}` : ''}`);

// FR-SCHED-03: preferred teaching windows, consumed by the CSP engine's soft scoring.
export const getFacultyPreferences = (facultyProfileId) =>
    get(`/api/faculty-load/${facultyProfileId}/preferences`);

// windows: [{ day, startMinutes, endMinutes }]
export function saveFacultyPreferences(facultyProfileId, windows) {
    return apiFetch(`/api/faculty-load/${facultyProfileId}/preferences`, {
        method: 'PUT',
        body: { windows }
    });
}

// items: [{ subjectId, classSectionId }]
export function saveFacultyLoad(facultyProfileId, semesterId, items) {
    return apiFetch(`/api/faculty-load/${facultyProfileId}`, {
        method: 'PUT',
        body: { semesterId, items }
    });
}
