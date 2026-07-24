import { getToken } from '../auth/api';
import { saveBlob, filenameFromDisposition } from '../shell/download';

async function downloadWorkbook(url, fallbackName) {
    const response = await fetch(url, {
        headers: { Authorization: `Bearer ${getToken()}` }
    });
    if (!response.ok) {
        let message = 'Export failed.';
        try { message = (await response.json())?.message || message; } catch { /* non-JSON body */ }
        throw new Error(message);
    }
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
