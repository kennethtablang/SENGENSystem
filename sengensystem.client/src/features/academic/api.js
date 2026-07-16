import { getToken } from '../auth/api';

// Academic setup (School Admin): school years, semesters, buildings, rooms.

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

function query(params) {
    const qs = new URLSearchParams();
    Object.entries(params || {}).forEach(([k, v]) => {
        if (v != null && v !== '' && v !== 'All') qs.set(k, v);
    });
    const s = qs.toString();
    return s ? `?${s}` : '';
}

// ---------- School years ----------
export const listSchoolYears = () => get('/api/school-years');
export const createSchoolYear = (data) => send('POST', '/api/school-years', data);
export const updateSchoolYear = (id, data) => send('PUT', `/api/school-years/${id}`, data);
export const deleteSchoolYear = (id) => send('DELETE', `/api/school-years/${id}`);
export const activateSchoolYear = (id) => send('POST', `/api/school-years/${id}/active`, {});

// ---------- Semesters ----------
export const listSemesters = (schoolYearId) => get(`/api/semesters${query({ schoolYearId })}`);
export const createSemester = (data) => send('POST', '/api/semesters', data);
export const updateSemester = (id, data) => send('PUT', `/api/semesters/${id}`, data);
export const deleteSemester = (id) => send('DELETE', `/api/semesters/${id}`);
export const activateSemester = (id) => send('POST', `/api/semesters/${id}/active`, {});

// ---------- Buildings ----------
export const listBuildings = () => get('/api/buildings');
export const createBuilding = (data) => send('POST', '/api/buildings', data);
export const updateBuilding = (id, data) => send('PUT', `/api/buildings/${id}`, data);
export const deleteBuilding = (id) => send('DELETE', `/api/buildings/${id}`);

// ---------- Rooms ----------
export const listRooms = (buildingId) => get(`/api/rooms${query({ buildingId })}`);
export const createRoom = (data) => send('POST', '/api/rooms', data);
export const updateRoom = (id, data) => send('PUT', `/api/rooms/${id}`, data);
export const deleteRoom = (id) => send('DELETE', `/api/rooms/${id}`);

// ---------- Class sections (student blocks) ----------
export const listClassSections = (semesterId, programCode) =>
    get(`/api/class-sections${query({ semesterId, programCode })}`);
export const createClassSection = (data) => send('POST', '/api/class-sections', data);
export const updateClassSection = (id, data) => send('PUT', `/api/class-sections/${id}`, data);
export const deleteClassSection = (id) => send('DELETE', `/api/class-sections/${id}`);
