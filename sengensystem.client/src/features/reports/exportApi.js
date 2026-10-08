import { apiDownload } from '../shell/apiClient';

/* The shared apiDownload: server-named file, 401 handling, and a timeout so a slow export cannot
   hold its button busy forever. */
const downloadWorkbook = (url, fallbackName) => apiDownload(url, fallbackName);

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
