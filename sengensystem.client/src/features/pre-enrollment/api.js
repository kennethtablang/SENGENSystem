import { apiFetch, apiDownload } from '../shell/apiClient';
import { pageParams } from '../shell/useServerTable';

/* Every call here goes through the shared client — one place for the auth header, the
   ProblemDetails error shape, and the global 401 handling that signs a lapsed session out
   rather than failing with a generic message on a page that will never work again. */
const authRequest = apiFetch;

// FR-PRE-02/04: Admission Officer pre-authorization for online slot selection.

export function listPreAuthorizations({ filter, ...page } = {}) {
    const params = pageParams(page);
    if (filter && filter !== 'All') params.set('filter', filter);
    const qs = params.toString() ? `?${params}` : '';
    return authRequest(`/api/pre-authorization${qs}`);
}

export function grantPreAuthorization(registrationId) {
    return authRequest(`/api/pre-authorization/${registrationId}`, { method: 'POST' });
}

export function revokePreAuthorization(registrationId) {
    return authRequest(`/api/pre-authorization/${registrationId}`, { method: 'DELETE' });
}

// ---- FR-PRE-01: the .xlsx import ----
// These two lived inline in PreEnrollmentPage with their own fetch and error handling, which is
// how they escaped the shared client. Moved here so the page calls an api module like every other
// page does, and so the import and template download get the same 401 handling as everything else.

/** Uploads the workbook. FormData is passed through untouched so the browser sets its own boundary. */
export function importPreEnrollment(file) {
    const form = new FormData();
    form.append('file', file);
    return apiFetch('/api/pre-enrollment/import', { method: 'POST', body: form });
}

export function downloadPreEnrollmentTemplate() {
    return apiDownload('/api/pre-enrollment/template', 'sengen-preenrollment-template.xlsx');
}
