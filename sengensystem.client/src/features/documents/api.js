import { apiFetch, apiDownload } from '../shell/apiClient';
import { pageParams } from '../shell/useServerTable';

/* Every call here goes through the shared client — one place for the auth header, the
   ProblemDetails error shape, and the global 401 handling that signs a lapsed session out
   rather than failing with a generic message on a page that will never work again. */
const authRequest = apiFetch;

// ---- Staff (Admission Officer / Registrar): checklist board (FR-DOC-01..03) ----

export function listChecklists({ completion, ...page } = {}) {
    const params = pageParams(page);
    if (completion && completion !== 'All') params.set('completion', completion);
    const qs = params.toString() ? `?${params}` : '';
    return authRequest(`/api/documents${qs}`);
}

/* Records a paper's status and/or the date it was received ("yyyy-MM-dd"). Pass a null status to
   change only the date; omit the date and the server records today when a paper is first received. */
export function updateDocumentStatus(documentId, status, submittedOn) {
    const body = {};
    if (status) body.status = status;
    if (submittedOn) body.submittedOn = submittedOn;
    return authRequest(`/api/documents/${documentId}`, { method: 'PUT', body });
}

// ---- Spreadsheet import / export of the checklist (FR-DOC-02) ----

function importForm(file, options) {
    const form = new FormData();
    form.append('file', file);
    form.append('options', JSON.stringify(options ?? {}));
    return form;
}

/* Reads the file and matches each row to a student; writes nothing. `options`:
   { sheets, overrides: { rowKey: registrationId | number }, allowDowngrade }. */
export function previewChecklistImport(file, options) {
    return authRequest('/api/documents/import/preview', { method: 'POST', body: importForm(file, options) });
}

/* Applies the rows in `options.include`. The server re-reads the file and re-plans against the
   checklists as they are now, so the same options as the last preview must be sent. */
export function commitChecklistImport(file, options) {
    return authRequest('/api/documents/import', { method: 'POST', body: importForm(file, options) });
}

/* The board as an .xlsx, filtered as the board is — and the file the import reads back by
   registration number. */
export function exportChecklists({ completion, search } = {}) {
    const params = new URLSearchParams();
    if (completion && completion !== 'All') params.set('completion', completion);
    if (search) params.set('search', search);
    const qs = params.toString() ? `?${params}` : '';
    return apiDownload(`/api/documents/export${qs}`, 'sengen-requirement-checklist.xlsx');
}

// FR-DOC-05: reminder emails; omit registrationId to sweep every incomplete checklist.
export function sendReminders(registrationId) {
    return authRequest('/api/documents/reminders', {
        method: 'POST',
        body: { registrationId: registrationId ?? null }
    });
}

// ---- Configurable requirement catalog (FR-DOC-01) ----

export function listRequirements() {
    return authRequest('/api/requirements');
}

// The body must carry every field the form edits. The server treats an omitted flag as "leave
// unchanged" and omitted studentTypes as "everyone", so dropping a field here silently discards
// the edit (this is how "required for pre-authorization" could never be switched off).
function requirementBody({
    name, description, programs, studentTypes, isActive,
    isRequiredForAuthorization, acceptsCertificateOfGrades
}) {
    return {
        name, description, programs, studentTypes, isActive,
        isRequiredForAuthorization, acceptsCertificateOfGrades
    };
}

export function createRequirement(requirement) {
    return authRequest('/api/requirements', {
        method: 'POST',
        body: requirementBody(requirement)
    });
}

export function updateRequirement(id, requirement) {
    return authRequest(`/api/requirements/${id}`, {
        method: 'PUT',
        body: requirementBody(requirement)
    });
}

export function archiveRequirement(id) {
    return authRequest(`/api/requirements/${id}`, { method: 'DELETE' });
}

// ---- Student: own record link + checklist (FR-ENL-05 identity link) ----

export function getMyLink() {
    return authRequest('/api/registration/link');
}

export function claimRecord(studentNumber, dateOfBirth) {
    return authRequest('/api/registration/link', { method: 'POST', body: { studentNumber, dateOfBirth } });
}
