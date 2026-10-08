import { apiFetch, apiDownload } from '../shell/apiClient';
import { pageParams } from '../shell/useServerTable';

// FR-ENL-01/06: the Registrar's record of what a student has already taken and how it ended.
// Prerequisite enforcement, repeat subjects in the enlistment plan, and the year-level ladder are
// all answered from this — see AcademicHistory on the server.

/** The queue. `view` is All · recorded · none — "none" is the backfill backlog. */
export function listAcademicRecords({ view, ...page } = {}) {
    const params = pageParams(page);
    if (view && view !== 'All') params.set('view', view);
    const qs = params.toString() ? `?${params}` : '';
    return apiFetch(`/api/academic-records${qs}`);
}

export function getAcademicRecord(registrationId) {
    return apiFetch(`/api/academic-records/${registrationId}`);
}

/**
 * Records verdicts for one term. A verdict of 'None' clears that term's row for the subject —
 * which is how a mistake is undone without touching an earlier attempt at the same subject.
 */
export function saveAcademicRecord(registrationId, { semesterId, items }) {
    return apiFetch(`/api/academic-records/${registrationId}`, {
        method: 'PUT',
        body: { semesterId, items }
    });
}

/** Bulk backfill from .xlsx. A row already on file is updated, not rejected. */
export function importAcademicRecords(file) {
    const form = new FormData();
    form.append('file', file);
    // FormData is passed through untouched — the shared client leaves the Content-Type off so the
    // browser can set its own multipart boundary.
    return apiFetch('/api/academic-records/import', { method: 'POST', body: form });
}

export function downloadAcademicRecordTemplate() {
    return apiDownload('/api/academic-records/template', 'sengen-academic-records-template.xlsx');
}
