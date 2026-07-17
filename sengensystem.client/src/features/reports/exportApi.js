import { getToken } from '../auth/api';

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
    const match = /filename=([^;]+)/.exec(response.headers.get('content-disposition') || '');
    const objectUrl = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = objectUrl;
    a.download = match ? match[1].trim().replace(/"/g, '') : fallbackName;
    a.click();
    URL.revokeObjectURL(objectUrl);
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
