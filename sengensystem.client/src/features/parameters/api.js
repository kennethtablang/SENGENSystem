import { getToken } from '../auth/api';

// System parameters (School Admin): the institutional inputs the scheduling engine runs on —
// allowable time slots, per-faculty unit-load ceilings, and the section seat cap (FR-SCHED-05).

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

async function send(method, url, body) {
    const response = await fetch(url, {
        method,
        headers: authHeaders(body != null),
        body: body != null ? JSON.stringify(body) : undefined
    });
    if (response.status === 204) return null;
    if (!response.ok) throw await parseError(response);
    return response.json();
}

/* The whole page in one request: seat cap, allowable time slots, faculty ceilings. */
export const getParameters = () => get('/api/parameters');

// ---------- Section seat cap ----------
export const setSectionCapacityCap = (cap) => send('PUT', '/api/parameters/section-capacity', { cap });

// ---------- Allowable time slots ----------
export const createTimeSlot = (data) => send('POST', '/api/parameters/time-slots', data);
export const updateTimeSlot = (id, data) => send('PUT', `/api/parameters/time-slots/${id}`, data);
export const deleteTimeSlot = (id) => send('DELETE', `/api/parameters/time-slots/${id}`);

// ---------- Faculty unit-load ceilings ----------
export const setFacultyLoadLimit = (id, maxLoadUnits) =>
    send('PUT', `/api/parameters/faculty/${id}/load-limit`, { maxLoadUnits });
