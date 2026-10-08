import { apiFetch } from '../shell/apiClient';

// System parameters (School Admin): the institutional inputs the scheduling engine runs on —
// allowable time slots, per-faculty unit-load ceilings, and the section seat cap (FR-SCHED-05).

/* Both helpers go through the shared client — see academic/api.js for the reasoning. */
const get = (url) => apiFetch(url);
const send = (method, url, body) => apiFetch(url, { method, body });

/* The whole page in one request: seat cap, allowable time slots, faculty ceilings. */
export const getParameters = () => get('/api/parameters');

// ---------- Section seat cap ----------
export const setSectionCapacityCap = (cap) => send('PUT', '/api/parameters/section-capacity', { cap });

// ---------- Enrollment rules + scheduling-engine budgets ----------
// Only the fields passed are changed, so each card saves independently.
export const updateSettings = (patch) => send('PUT', '/api/parameters/settings', patch);

// ---------- Allowable time slots ----------
export const createTimeSlot = (data) => send('POST', '/api/parameters/time-slots', data);
export const updateTimeSlot = (id, data) => send('PUT', `/api/parameters/time-slots/${id}`, data);
export const deleteTimeSlot = (id) => send('DELETE', `/api/parameters/time-slots/${id}`);

// ---------- Faculty unit-load ceilings ----------
export const setFacultyLoadLimit = (id, maxLoadUnits) =>
    send('PUT', `/api/parameters/faculty/${id}/load-limit`, { maxLoadUnits });
