import { apiFetch } from '../shell/apiClient';
import { saveBlob, filenameFromDisposition } from '../shell/download';

// Analytics: institution-wide classroom usage (FR-DASH-02 room utilization).

/** Every room scored for the given (or active) semester, with banded summary counts. */
export function getRoomUtilization(semesterId) {
    const qs = semesterId ? `?semesterId=${encodeURIComponent(semesterId)}` : '';
    return apiFetch(`/api/analytics/room-utilization${qs}`);
}

/**
 * The same analysis as a workbook: an Overview sheet plus Monday–Friday breakdowns,
 * with under-used rooms filled red (FR-RPT-02).
 */
export function downloadRoomUtilizationWorkbook(semesterId) {
    return downloadXlsx('/api/analytics/room-utilization/export', semesterId,
        'sengen-room-utilization.xlsx');
}

/**
 * The visual timetable: time slots against room columns, one sheet per day, colour-coded
 * blocks carrying subject, faculty, and section. Print-ready (FR-RPT-02).
 */
export function downloadRoomGridSchedule(semesterId) {
    return downloadXlsx('/api/reports/room-grid-schedule', semesterId,
        'sengen-room-grid-schedule.xlsx');
}

/* Kept local rather than using the shared apiDownload: these endpoints name the file themselves in
   Content-Disposition, and honouring that beats the caller's guess. `raw: true` gets the Response
   back while still going through the shared 401 handling. */
async function downloadXlsx(path, semesterId, fallbackName) {
    const qs = semesterId ? `?semesterId=${encodeURIComponent(semesterId)}` : '';
    const response = await apiFetch(`${path}${qs}`, { raw: true });

    const blob = await response.blob();
    const name = filenameFromDisposition(response.headers.get('Content-Disposition'), fallbackName);
    saveBlob(blob, name);
}
