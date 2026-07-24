import { getToken } from '../auth/api';

// Schedule board (Academic Head / School Admin): drag faculty-allocated subjects onto a
// weekly calendar. Placements are persisted as ScheduleAssignments (FR-SCHED-02, FR-FAC-02).

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

async function send(method, url, body) {
    const response = await fetch(url, {
        method,
        headers: authHeaders(!!body),
        ...(body ? { body: JSON.stringify(body) } : {})
    });
    if (!response.ok) throw await parseError(response);
    return response.status === 204 ? null : response.json();
}

export const getBoard = (semesterId) =>
    send('GET', `/api/scheduling/board${semesterId ? `?semesterId=${semesterId}` : ''}`);

// body: { facultyLoadAssignmentId, component, roomId, day, startMinutes, endMinutes }
// `component` is "Lecture" or "Laboratory" — a lecture-laboratory subject is placed as two
// separate meetings, and the server refuses a room that doesn't suit the one being placed.
export const placeEntry = (body) => send('POST', '/api/scheduling/board', body);

// body: { roomId, day, startMinutes, endMinutes }
export const moveEntry = (assignmentId, body) => send('PUT', `/api/scheduling/board/${assignmentId}`, body);

export const removeEntry = (assignmentId) => send('DELETE', `/api/scheduling/board/${assignmentId}`);
