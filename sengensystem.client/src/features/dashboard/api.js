import { apiFetch } from '../shell/apiClient';

// FR-DASH-01/02: live metrics scoped to the active (or selected) semester.
export function getDashboardMetrics(semesterId) {
    const qs = semesterId ? `?semesterId=${encodeURIComponent(semesterId)}` : '';
    return apiFetch(`/api/dashboard/metrics${qs}`);
}

// FR-DASH-03: how each schedule row came to be + the constraints behind it.
export function getSchedulingTransparency(semesterId) {
    const qs = semesterId ? `?semesterId=${encodeURIComponent(semesterId)}` : '';
    return apiFetch(`/api/dashboard/scheduling-transparency${qs}`);
}
