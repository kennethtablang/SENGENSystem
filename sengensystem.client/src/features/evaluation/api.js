import { getToken } from '../auth/api';

// FR-EVAL: the Registrar's transferee credit evaluation, and the printable subject listings
// (FR-RPT-05) that read off it — the prospectus, the evaluation sheet, and a student's
// certificate of registration.

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
        reasons: payload?.reasons || [],
        fieldErrors: payload?.errors || {}
    };
}

function authHeaders() {
    return { Authorization: `Bearer ${getToken()}` };
}

async function request(url, options = {}) {
    const response = await fetch(url, {
        ...options,
        headers: {
            ...(options.body ? { 'Content-Type': 'application/json' } : {}),
            ...authHeaders(),
            ...(options.headers || {})
        },
        body: options.body ? JSON.stringify(options.body) : undefined
    });
    if (!response.ok) throw await parseError(response);
    return response.status === 204 ? null : response.json();
}

export function listEvaluations({ status, search } = {}) {
    const params = new URLSearchParams();
    if (status && status !== 'All') params.set('status', status);
    if (search) params.set('search', search);
    const qs = params.toString() ? `?${params}` : '';
    return request(`/api/transferee-evaluations${qs}`);
}

export function getEvaluation(registrationId) {
    return request(`/api/transferee-evaluations/${registrationId}`);
}

/** Save decisions incrementally — a partly-ruled sheet is a valid state, not an error. */
export function saveEvaluation(registrationId, { items, remarks }) {
    return request(`/api/transferee-evaluations/${registrationId}`, {
        method: 'PUT',
        body: { items, remarks }
    });
}

/** Sign off: sets the student's year level and opens their enlistment gate. */
export function completeEvaluation(registrationId, { assignedYearLevel, remarks } = {}) {
    return request(`/api/transferee-evaluations/${registrationId}/complete`, {
        method: 'POST',
        body: { assignedYearLevel, remarks }
    });
}

export function reopenEvaluation(registrationId) {
    return request(`/api/transferee-evaluations/${registrationId}/reopen`, { method: 'POST' });
}

// ---- Printable listings ----

export function listProspectusPrograms() {
    return request('/api/prospectus/programs');
}

/**
 * Downloads a PDF and hands it to the browser. Blob rather than a plain link because every one of
 * these routes is bearer-authenticated — a bare href would arrive without the token.
 */
async function downloadPdf(url, filename) {
    const response = await fetch(url, { headers: authHeaders() });
    if (!response.ok) throw await parseError(response);
    const blob = await response.blob();
    const href = URL.createObjectURL(blob);
    const anchor = document.createElement('a');
    anchor.href = href;
    anchor.download = filename;
    document.body.appendChild(anchor);
    anchor.click();
    anchor.remove();
    setTimeout(() => URL.revokeObjectURL(href), 0);
}

export function downloadProspectus({ curriculumId, yearLevel, programCode }) {
    const params = new URLSearchParams();
    if (curriculumId) params.set('curriculumId', curriculumId);
    if (yearLevel) params.set('yearLevel', String(yearLevel));
    const suffix = yearLevel ? `-year${yearLevel}` : '';
    return downloadPdf(
        `/api/prospectus/curriculum.pdf?${params}`,
        `sengen-prospectus-${(programCode || 'program').toLowerCase()}${suffix}.pdf`);
}

export function downloadEvaluationSheet(registrationId, studentNumber) {
    return downloadPdf(
        `/api/prospectus/students/${registrationId}/evaluation.pdf`,
        `sengen-evaluation-${studentNumber || registrationId}.pdf`);
}

export function downloadRegistrationForm(registrationId, studentNumber) {
    return downloadPdf(
        `/api/prospectus/students/${registrationId}/registration-form.pdf`,
        `sengen-registration-${studentNumber || registrationId}.pdf`);
}

// ---- A student's own copies ----

export function downloadMySubjects() {
    return downloadPdf('/api/prospectus/me/curriculum.pdf', 'sengen-my-subjects.pdf');
}

export function downloadMyRegistrationForm() {
    return downloadPdf('/api/prospectus/me/registration-form.pdf', 'sengen-registration-form.pdf');
}
