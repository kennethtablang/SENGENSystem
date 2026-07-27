import { useCallback, useEffect, useRef, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { Icon } from '../shell/AppLayout';
import { listNotifications, markAllRead, markRead } from './api';
import { iconFor, relativeTime, NOTIFICATIONS_CHANGED, announceNotificationsChanged } from './meta';
import { subscribeToReports } from '../reports/live';
import { bellRefreshMs, showsBadges, marksReadOnOpen } from '../settings/prefs';
import './notifications.css';

/* Top-bar bell: unread badge + a dropdown of the latest notices. */
function NotificationsBell() {
    const navigate = useNavigate();
    const boxRef = useRef(null);
    const [open, setOpen] = useState(false);
    const [unreadCount, setUnreadCount] = useState(0);
    const [items, setItems] = useState([]);

    const load = useCallback(async () => {
        try {
            const data = await listNotifications({ take: 8 });
            setUnreadCount(data.unreadCount);
            setItems(data.notifications);
        } catch {
            // Bell refreshes are silent best-effort; the page surfaces real errors.
        }
    }, []);

    // Initial load + gentle polling + refresh whenever another view marks notices read.
    // A SignalR push ("notifications" area) refreshes the badge the moment a notice is
    // dispatched; polling stays as the fallback when the socket is down.
    useEffect(() => {
        const initial = setTimeout(load, 0);
        const timer = setInterval(load, bellRefreshMs());
        window.addEventListener(NOTIFICATIONS_CHANGED, load);
        const unsubscribe = subscribeToReports(payload => {
            if (payload?.area === 'notifications') setTimeout(load, 600);
        });
        return () => {
            clearTimeout(initial);
            clearInterval(timer);
            window.removeEventListener(NOTIFICATIONS_CHANGED, load);
            unsubscribe();
        };
    }, [load]);

    useEffect(() => {
        if (!open) return undefined;
        const refresh = setTimeout(load, 0);
        const onDoc = e => {
            if (boxRef.current && !boxRef.current.contains(e.target)) setOpen(false);
        };
        const onKey = e => {
            if (e.key === 'Escape') setOpen(false);
        };
        document.addEventListener('mousedown', onDoc);
        document.addEventListener('keydown', onKey);
        return () => {
            clearTimeout(refresh);
            document.removeEventListener('mousedown', onDoc);
            document.removeEventListener('keydown', onKey);
        };
    }, [open, load]);

    const openItem = async item => {
        setOpen(false);
        if (!item.isRead && marksReadOnOpen()) {
            setItems(prev => prev.map(n => (n.id === item.id ? { ...n, isRead: true } : n)));
            setUnreadCount(c => Math.max(0, c - 1));
            try {
                await markRead(item.id);
                announceNotificationsChanged();
            } catch {
                // best-effort; the list will self-correct on the next refresh
            }
        }
        navigate(item.linkTo || '/notifications');
    };

    const readAll = async () => {
        setItems(prev => prev.map(n => ({ ...n, isRead: true })));
        setUnreadCount(0);
        try {
            await markAllRead();
            announceNotificationsChanged();
        } catch {
            // best-effort; the list will self-correct on the next refresh
        }
    };

    return (
        <div className="notif-bell" ref={boxRef}>
            <button
                type="button"
                className={`shell-iconbtn notif-bell-btn${open ? ' active' : ''}`}
                onClick={() => setOpen(o => !o)}
                aria-haspopup="menu"
                aria-expanded={open}
                aria-label={unreadCount > 0 ? `Notifications (${unreadCount} unread)` : 'Notifications'}
                title="Notifications"
            >
                <Icon name="bell" />
                {unreadCount > 0 && showsBadges() && (
                    <span className="notif-badge">{unreadCount > 99 ? '99+' : unreadCount}</span>
                )}
            </button>

            {open && (
                <div className="notif-drop" role="menu" aria-label="Notifications">
                    <div className="notif-drop-head">
                        <strong>Notifications</strong>
                        {unreadCount > 0 && (
                            <button type="button" className="link-btn" onClick={readAll}>
                                Mark all read
                            </button>
                        )}
                    </div>

                    {items.length === 0 ? (
                        <p className="notif-drop-empty">You&rsquo;re all caught up — nothing here yet.</p>
                    ) : (
                        <ul className="notif-drop-list">
                            {items.map(item => (
                                <li key={item.id}>
                                    <button
                                        type="button"
                                        role="menuitem"
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

                    <button
                        type="button"
                        className="notif-drop-all"
                        onClick={() => {
                            setOpen(false);
                            navigate('/notifications');
                        }}
                    >
                        View all notifications
                    </button>
                </div>
            )}
        </div>
    );
}

export default NotificationsBell;
