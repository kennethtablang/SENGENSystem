import { useCallback, useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { Icon } from '../shell/AppLayout';
import { listNotifications, markAllRead, markRead } from './api';
import { iconFor, relativeTime, announceNotificationsChanged } from './meta';
import './notifications.css';

function NotificationsPage() {
    const navigate = useNavigate();
    const [data, setData] = useState(null);
    const [error, setError] = useState(null);
    const [filter, setFilter] = useState('all'); // 'all' | 'unread'

    const load = useCallback(async () => {
        setError(null);
        try {
            setData(await listNotifications({ take: 100 }));
        } catch (err) {
            setError(err.message);
        }
    }, []);

    useEffect(() => {
        const initial = setTimeout(load, 0);
        return () => clearTimeout(initial);
    }, [load]);

    const items = data?.notifications ?? [];
    const shown = filter === 'unread' ? items.filter(n => !n.isRead) : items;
    const unreadCount = data?.unreadCount ?? 0;

    const openItem = async item => {
        if (!item.isRead) {
            setData(prev => prev && {
                ...prev,
                unreadCount: Math.max(0, prev.unreadCount - 1),
                notifications: prev.notifications.map(n => (n.id === item.id ? { ...n, isRead: true } : n))
            });
            try {
                await markRead(item.id);
                announceNotificationsChanged();
            } catch {
                // best-effort; refresh will self-correct
            }
        }
        if (item.linkTo) navigate(item.linkTo);
    };

    const readAll = async () => {
        setData(prev => prev && {
            ...prev,
            unreadCount: 0,
            notifications: prev.notifications.map(n => ({ ...n, isRead: true }))
        });
        try {
            await markAllRead();
            announceNotificationsChanged();
        } catch {
            // best-effort; refresh will self-correct
        }
    };

    return (
        <div className="notif-page">
            <header className="notif-head rise">
                <div>
                    <h2>Notifications</h2>
                    <p>
                        {unreadCount > 0
                            ? `${unreadCount} unread notice${unreadCount === 1 ? '' : 's'}.`
                            : 'You’re all caught up.'}
                    </p>
                </div>
                {unreadCount > 0 && (
                    <button type="button" className="btn btn-ghost" onClick={readAll}>
                        Mark all as read
                    </button>
                )}
            </header>

            <div className="notif-filters rise rise-1" role="tablist" aria-label="Filter notifications">
                <button
                    type="button"
                    role="tab"
                    aria-selected={filter === 'all'}
                    className={`chip ${filter === 'all' ? 'chip-blue' : 'chip-muted'}`}
                    onClick={() => setFilter('all')}
                >
                    All ({items.length})
                </button>
                <button
                    type="button"
                    role="tab"
                    aria-selected={filter === 'unread'}
                    className={`chip ${filter === 'unread' ? 'chip-blue' : 'chip-muted'}`}
                    onClick={() => setFilter('unread')}
                >
                    Unread ({unreadCount})
                </button>
            </div>

            {error && <div className="alert">{error}</div>}

            {data && shown.length === 0 && !error && (
                <div className="card notif-empty rise rise-2">
                    <span className="notif-empty-mark">
                        <Icon name="bell" />
                    </span>
                    <h3>{filter === 'unread' ? 'No unread notifications' : 'Nothing here yet'}</h3>
                    <p>
                        Notices about your schedule, enlistment, and documents will show up here
                        the moment something happens.
                    </p>
                </div>
            )}

            {shown.length > 0 && (
                <ul className="card notif-list rise rise-2">
                    {shown.map(item => (
                        <li key={item.id}>
                            <button
                                type="button"
                                className={`notif-item${item.isRead ? '' : ' unread'}`}
                                onClick={() => openItem(item)}
                            >
                                <span className="notif-item-icon">
                                    <Icon name={iconFor(item.kind)} />
                                </span>
                                <span className="notif-item-main">
                                    <span className="notif-item-title">{item.title}</span>
                                    <span className="notif-item-body">{item.body}</span>
                                    <span className="notif-item-time">{relativeTime(item.createdAtUtc)}</span>
                                </span>
                                {!item.isRead && <span className="notif-dot" aria-label="Unread" />}
                            </button>
                        </li>
                    ))}
                </ul>
            )}
        </div>
    );
}

export default NotificationsPage;
