import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
import { getToken } from '../auth/api';

// Live channel for the reports pages: the server pushes "reportsChanged" whenever
// report-relevant data mutates, and subscribers refetch what they're showing.
//
// Returns an unsubscribe function; the connection is shared and stops once the
// last subscriber leaves.
let connection = null;
let subscribers = 0;
const handlers = new Set();

export function subscribeToReports(onChange, onStateChange) {
    handlers.add(onChange);
    subscribers++;

    if (!connection) {
        connection = new HubConnectionBuilder()
            .withUrl('/hubs/reports', { accessTokenFactory: () => getToken() })
            .withAutomaticReconnect()
            .configureLogging(LogLevel.None)
            .build();
        connection.on('reportsChanged', payload => {
            handlers.forEach(h => h(payload));
        });
    }

    const notifyState = state => onStateChange?.(state);
    connection.onreconnecting?.(() => notifyState('reconnecting'));
    connection.onreconnected?.(() => notifyState('live'));
    connection.onclose?.(() => notifyState('offline'));

    if (connection.state === 'Disconnected') {
        connection.start()
            .then(() => notifyState('live'))
            .catch(() => notifyState('offline'));
    } else if (connection.state === 'Connected') {
        notifyState('live');
    }

    return () => {
        handlers.delete(onChange);
        subscribers--;
        if (subscribers <= 0 && connection) {
            const dying = connection;
            connection = null;
            subscribers = 0;
            dying.stop().catch(() => { });
        }
    };
}
