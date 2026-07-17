import { getToken } from '../auth/api';

// Subjects & Curriculum (Academic Head): program curricula and their subjects.

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

// ---------- Curricula ----------
export const listCurricula = () => get('/api/curricula');
export const createCurriculum = (data) => send('POST', '/api/curricula', data);
export const updateCurriculum = (id, data) => send('PUT', `/api/curricula/${id}`, data);
export const deleteCurriculum = (id) => send('DELETE', `/api/curricula/${id}`);
export const activateCurriculum = (id) => send('POST', `/api/curricula/${id}/active`, {});

// ---------- Subjects ----------
export const listSubjects = (curriculumId) =>
    get(`/api/subjects${curriculumId ? `?curriculumId=${curriculumId}` : ''}`);
export const createSubject = (data) => send('POST', '/api/subjects', data);
export const updateSubject = (id, data) => send('PUT', `/api/subjects/${id}`, data);
export const deleteSubject = (id) => send('DELETE', `/api/subjects/${id}`);
export const archiveSubject = (id, reason) => send('POST', `/api/subjects/${id}/archive`, { reason: reason || null });
export const restoreSubject = (id) => send('POST', `/api/subjects/${id}/restore`, {});
