import { apiFetch } from '../shell/apiClient';

// Academic setup (School Admin): school years, semesters, buildings, rooms.

/* Both helpers go through the shared client — one place for the auth header, the ProblemDetails
   error shape, and the global 401 handling that signs a lapsed session out rather than failing
   with a generic message on a page that will never work again. A 204 still comes back as null. */
const get = (url) => apiFetch(url);
const send = (method, url, body) => apiFetch(url, { method, body });

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
export const archiveSemester = (id) => send('POST', `/api/semesters/${id}/archive`, {});
export const unarchiveSemester = (id) => send('POST', `/api/semesters/${id}/unarchive`, {});

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
