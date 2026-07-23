import { getToken } from '../auth/api';
import { saveBlob, filenameFromDisposition } from '../shell/download';

// Analytics: institution-wide classroom usage (FR-DASH-02 room utilization).

async function parseError(response) {
    let payload = null;
    try {
        payload = await response.json();
    } catch {
        // non-JSON error body
    }
    return {
        status: response.status,
        message: payload?.message || payload?.title || 'Something went wrong. Please try again.'
    };
}

async function get(url) {
    const response = await fetch(url, { headers: { Authorization: `Bearer ${getToken()}` } });
    if (!response.ok) throw await parseError(response);
    return response.json();
}

/** Every room scored for the given (or active) semester, with banded summary counts. */
export function getRoomUtilization(semesterId) {
    const qs = semesterId ? `?semesterId=${encodeURIComponent(semesterId)}` : '';
    return get(`/api/analytics/room-utilization${qs}`);
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

async function downloadXlsx(path, semesterId, fallbackName) {
    const qs = semesterId ? `?semesterId=${encodeURIComponent(semesterId)}` : '';
    const response = await fetch(`${path}${qs}`, {
        headers: { Authorization: `Bearer ${getToken()}` }
    });
    if (!response.ok) throw await parseError(response);

    const blob = await response.blob();
    const name = filenameFromDisposition(response.headers.get('Content-Disposition'), fallbackName);
    saveBlob(blob, name);
}
