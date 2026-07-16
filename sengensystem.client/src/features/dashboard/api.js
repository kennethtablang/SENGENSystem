import { getToken } from '../auth/api';

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

// FR-DASH-01/02: live metrics scoped to the active (or selected) semester.
export function getDashboardMetrics(semesterId) {
    const qs = semesterId ? `?semesterId=${encodeURIComponent(semesterId)}` : '';
    return get(`/api/dashboard/metrics${qs}`);
}

// FR-DASH-03: how each schedule row came to be + the constraints behind it.
export function getSchedulingTransparency(semesterId) {
    const qs = semesterId ? `?semesterId=${encodeURIComponent(semesterId)}` : '';
    return get(`/api/dashboard/scheduling-transparency${qs}`);
}
