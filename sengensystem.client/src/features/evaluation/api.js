import { apiFetch, apiDownload } from '../shell/apiClient';
import { pageParams } from '../shell/useServerTable';

// FR-EVAL: the Registrar's transferee credit evaluation, and the printable subject listings
// (FR-RPT-05) that read off it — the prospectus, the evaluation sheet, and a student's
// certificate of registration.

/* Every call here goes through the shared client — one place for the auth header, the
   ProblemDetails error shape, and the global 401 handling that signs a lapsed session out rather
   than failing with a generic message on a page that will never work again. */
const request = apiFetch;

export function listEvaluations({ status, ...page } = {}) {
    const params = pageParams(page);
    if (status && status !== 'All') params.set('status', status);
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

/* Blob rather than a plain link because every one of these routes is bearer-authenticated — a bare
   href would arrive without the token. Now shared, so a download on an expired session raises the
   same 401 as anything else instead of silently saving a corrupt file. */
const downloadPdf = apiDownload;

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
