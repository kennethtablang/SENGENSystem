import { apiFetch } from '../shell/apiClient';

// The active term's enrollment stage (top-bar banner + the Registrar's phase control).

export function getEnrollmentStage() {
    return apiFetch('/api/enrollment-stage');
}

const post = (url, body) => apiFetch(url, { method: 'POST', body });

export const advanceEnrollmentStage = () => post('/api/enrollment-stage/advance');
export const setEnrollmentStage = (stage) => post('/api/enrollment-stage', { stage });

// Lets any mounted stage indicator refresh after another one changes the stage.
export const STAGE_EVENT = 'sengen:enrollment-stage';
export const announceStageChange = () => window.dispatchEvent(new Event(STAGE_EVENT));
