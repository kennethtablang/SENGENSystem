import { getToken } from '../auth/api';

// The active term's enrollment stage (top-bar banner + the Registrar's phase control).

async function parseError(response) {
    let payload = null;
    try {
        payload = await response.json();
    } catch {
        // non-JSON error body
    }
    return {
        status: response.status,
        message: payload?.message || payload?.title || 'Something went wrong. Please try again.',
        fieldErrors: payload?.errors || {}
    };
}

function authHeaders(json) {
    const h = { Authorization: `Bearer ${getToken()}` };
    if (json) h['Content-Type'] = 'application/json';
    return h;
}

export async function getEnrollmentStage() {
    const response = await fetch('/api/enrollment-stage', { headers: authHeaders() });
    if (!response.ok) throw await parseError(response);
    return response.json();
}

async function post(url, body) {
    const response = await fetch(url, {
        method: 'POST',
        headers: authHeaders(body != null),
        body: body != null ? JSON.stringify(body) : undefined
    });
    if (!response.ok) throw await parseError(response);
    return response.json();
}

export const advanceEnrollmentStage = () => post('/api/enrollment-stage/advance');
export const setEnrollmentStage = (stage) => post('/api/enrollment-stage', { stage });

// Lets any mounted stage indicator refresh after another one changes the stage.
export const STAGE_EVENT = 'sengen:enrollment-stage';
export const announceStageChange = () => window.dispatchEvent(new Event(STAGE_EVENT));
