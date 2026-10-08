import { apiFetch } from '../shell/apiClient';

// Subjects & Curriculum (Academic Head): program curricula and their subjects.

/* Both helpers go through the shared client — see academic/api.js for the reasoning. */
const get = (url) => apiFetch(url);
const send = (method, url, body) => apiFetch(url, { method, body });

// ---------- Curricula ----------
export const listCurricula = () => get('/api/curricula');
export const createCurriculum = (data) => send('POST', '/api/curricula', data);
export const updateCurriculum = (id, data) => send('PUT', `/api/curricula/${id}`, data);
export const activateCurriculum = (id) => send('POST', `/api/curricula/${id}/active`, {});
// Curricula are archived, never deleted — their subjects and history stay intact.
export const archiveCurriculum = (id, reason) => send('POST', `/api/curricula/${id}/archive`, { reason: reason || null });
export const restoreCurriculum = (id) => send('POST', `/api/curricula/${id}/restore`, {});

// ---------- Subjects ----------
export const listSubjects = (curriculumId) =>
    get(`/api/subjects${curriculumId ? `?curriculumId=${curriculumId}` : ''}`);
export const createSubject = (data) => send('POST', '/api/subjects', data);
export const updateSubject = (id, data) => send('PUT', `/api/subjects/${id}`, data);
export const archiveSubject = (id, reason) => send('POST', `/api/subjects/${id}/archive`, { reason: reason || null });
export const restoreSubject = (id) => send('POST', `/api/subjects/${id}/restore`, {});
