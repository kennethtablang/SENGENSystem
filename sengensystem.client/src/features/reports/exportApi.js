import { apiFetch } from '../shell/apiClient';
import { saveBlob, filenameFromDisposition } from '../shell/download';

/* `raw: true` because these endpoints name the file themselves in Content-Disposition and that
   beats the caller's guess — but the request still goes through the shared client, so an export
   started on a lapsed session now raises a 401 instead of silently saving whatever the server
   returned instead of a workbook. */
async function downloadWorkbook(url, fallbackName) {
    const response = await apiFetch(url, { raw: true });
    const blob = await response.blob();
    const name = filenameFromDisposition(response.headers.get('content-disposition'), fallbackName);
    saveBlob(blob, name);
}

/* Downloads the one-workbook "everything" bundle for a semester (FR-RPT-02):
   overview, registrations, master schedule, faculty loads, enlistment, room
   utilization, and document completion in a single .xlsx file. */
export function downloadSemesterExport(semesterId) {
    const query = semesterId ? `?semesterId=${encodeURIComponent(semesterId)}` : '';
    return downloadWorkbook(`/api/reports/semester-export${query}`, 'sengen-semester-export.xlsx');
}

/* Downloads the system-parameters workbook (admin-only): academic calendar,
   buildings & rooms, time slots, curricula & subjects, class sections, faculty
   profiles, and user accounts. */
export function downloadSystemParametersExport() {
    return downloadWorkbook('/api/reports/system-export', 'sengen-system-parameters.xlsx');
}
