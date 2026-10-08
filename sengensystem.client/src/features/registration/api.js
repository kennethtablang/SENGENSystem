import { apiFetch } from '../shell/apiClient';
import { pageParams } from '../shell/useServerTable';

/* Every call goes through the shared client — see academic/api.js for the reasoning. The three
   public endpoints below pass `auth: false`: the SIS and term-activation forms are used by people
   with no account at all, and sending an empty bearer header would be meaningless at best. */

// ---- Public (no account) ----

// FR-SIS-01: a new student / transferee self-submits the digital SIS.
export function registerStudent(data) {
    return apiFetch('/api/registration', { method: 'POST', body: data, auth: false });
}

// Step one: identify the returning student (student number + last name) and get back the year
// level and term they are about to activate into, so they check before anything is filed.
export function lookupTermActivation(data) {
    return apiFetch('/api/registration/term-activation/lookup', {
        method: 'POST', body: data, auth: false
    });
}

// Step two: finalize. Carries the confirmed year level and the term id from the lookup — the
// server refuses the request if that term is no longer the active one.
export function requestTermActivation(data) {
    return apiFetch('/api/registration/term-activation', {
        method: 'POST', body: data, auth: false
    });
}

// ---- Term activation control (Registrar / Academic Head / admins) ----

// The institution-wide switch for the public self-service activation form.
export function getTermActivationControl() {
    return apiFetch('/api/registration/term-activation/control');
}

export function setTermActivationControl(open) {
    return apiFetch('/api/registration/term-activation/control', {
        method: 'PUT', body: { open }
    });
}

// ---- Admission Officer ----

export function listTermActivations({ status, ...page } = {}) {
    const params = pageParams(page);
    if (status) params.set('status', status);
    const qs = params.toString() ? `?${params}` : '';
    return apiFetch(`/api/registration/term-activation${qs}`);
}

export function validateTermActivation(id, data) {
    return apiFetch(`/api/registration/term-activation/${id}/validate`, {
        method: 'POST', body: data
    });
}

// Admission Officer records the official student number (issued by a separate system).
// `status` chooses the view: 'pending' (still to number, the default), 'numbered', or 'all'.
export function listAssignableRegistrations({ status, ...page } = {}) {
    const params = pageParams(page);
    if (status) params.set('status', status);
    const qs = params.toString() ? `?${params}` : '';
    return apiFetch(`/api/registration/student-number${qs}`);
}

export function assignStudentNumber(id, studentNumber) {
    return apiFetch(`/api/registration/${id}/student-number`, {
        method: 'POST', body: { studentNumber }
    });
}

// ---- Registrar ----

/* Paged, filtered, and sorted by the server (see useServerTable). Spread the hook's `query` in:
   listRegistrations({ status, ...table.query }). */
export function listRegistrations({ status, ...page } = {}) {
    const params = pageParams(page);
    if (status && status !== 'All') params.set('status', status);
    const qs = params.toString() ? `?${params}` : '';
    return apiFetch(`/api/registration${qs}`);
}

export function getRegistration(id) {
    return apiFetch(`/api/registration/${id}`);
}

export function updateRegistration(id, data) {
    return apiFetch(`/api/registration/${id}`, { method: 'PUT', body: data });
}

// ---- Student (own record) ----

// F-04: the signed-in student's own SIS, with whether it is still open to their corrections.
export function getMyRegistration() {
    return apiFetch('/api/registration/mine');
}

// F-04: only the fields passed change; refused once the Registrar has confirmed the record.
export function updateMyRegistration(patch) {
    return apiFetch('/api/registration/mine', { method: 'PUT', body: patch });
}
