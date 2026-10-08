import { apiFetch } from '../shell/apiClient';
import { pageParams } from '../shell/useServerTable';

// FR-AUD-01: the School Admin reads the accountability log (newest first).
export function getAuditTrail({ action, ...page } = {}) {
    const params = pageParams(page);
    if (action && action !== 'All') params.set('action', action);
    const qs = params.toString() ? `?${params}` : '';
    return apiFetch(`/api/audit${qs}`);
}
