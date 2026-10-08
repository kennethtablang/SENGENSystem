import { useEffect, useState } from 'react';
import { getToken } from './token';
import { subscribeToReports } from '../reports/live';
import { NOTIFICATIONS_CHANGED } from '../notifications/meta';
import { bellRefreshMs } from '../settings/prefs';

// Role-scoped outstanding-work counts the sidebar renders next to a function. Shares the bell's
// 60s cadence and refreshes the moment a relevant SignalR area pushes (notifications / enlistment /
// registration), so a number appears without waiting for the next poll. Keyed to match `nav.js`
// item.badge values: 'notifications' | 'approvals' | 'registrations' | 'termActivations' |
// 'survey' | 'evaluations'.
const LIVE_AREAS = new Set(['notifications', 'enlistment', 'registration']);
const EMPTY = {
    notifications: 0, approvals: 0, registrations: 0, termActivations: 0, survey: 0, evaluations: 0
};

export function useNavBadges() {
    const [badges, setBadges] = useState(EMPTY);

    useEffect(() => {
        let active = true;

        /* Deliberately kept off the shared client, for the same reason `fetchCurrentUser` is: this
           polls every 60 seconds in the background, and routing its 401 through the global handler
           would mean a session expiring while the user reads a page yanks them to the login screen
           mid-sentence, triggered by a request they never made. A badge is best-effort — the next
           thing they actually click will do the redirect, with their action as the cause. */
        const load = async () => {
            try {
                const response = await fetch('/api/nav/badges', {
                    headers: { Authorization: `Bearer ${getToken()}` }
                });
                if (!response.ok) return;
                const data = await response.json();
                if (active) setBadges(data);
            } catch {
                // Badges are best-effort; the pages surface real errors.
            }
        };

        const initial = setTimeout(load, 0);
        const timer = setInterval(load, bellRefreshMs());
        window.addEventListener(NOTIFICATIONS_CHANGED, load);
        const unsubscribe = subscribeToReports(payload => {
            if (!payload?.area || LIVE_AREAS.has(payload.area)) setTimeout(load, 600);
        });

        return () => {
            active = false;
            clearTimeout(initial);
            clearInterval(timer);
            window.removeEventListener(NOTIFICATIONS_CHANGED, load);
            unsubscribe();
        };
    }, []);

    return badges;
}
