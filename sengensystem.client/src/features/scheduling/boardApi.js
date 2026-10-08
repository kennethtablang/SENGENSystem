import { apiFetch } from '../shell/apiClient';

// Schedule board (Academic Head / School Admin): drag faculty-allocated subjects onto a
// weekly calendar. Placements are persisted as ScheduleAssignments (FR-SCHED-02, FR-FAC-02).


/* Every call here goes through the shared client — one place for the auth header, the
   ProblemDetails error shape, and the global 401 handling that signs a lapsed session out rather
   than failing with a generic message on a page that will never work again. */
const send = (method, url, body) => apiFetch(url, { method, body });

export const getBoard = (semesterId) =>
    send('GET', `/api/scheduling/board${semesterId ? `?semesterId=${semesterId}` : ''}`);

// body: { facultyLoadAssignmentId, component, roomId, day, startMinutes, endMinutes }
// `component` is "Lecture" or "Laboratory" — a lecture-laboratory subject is placed as two
// separate meetings, and the server refuses a room that doesn't suit the one being placed.
export const placeEntry = (body) => send('POST', '/api/scheduling/board', body);

// body: { roomId, day, startMinutes, endMinutes }
// Returns { entry, amended, change, notifiedCount }. `amended` is true when the class was already
// published — the server flags the row and notifies the faculty member and enrolled students, so
// the board can tell the Academic Head that the move went out to people (FR-PUB-04).
export const moveEntry = (assignmentId, body) => send('PUT', `/api/scheduling/board/${assignmentId}`, body);

// Returns { removed, amended, notifiedCount } — same amendment semantics as a move.
export const removeEntry = (assignmentId) => send('DELETE', `/api/scheduling/board/${assignmentId}`);
