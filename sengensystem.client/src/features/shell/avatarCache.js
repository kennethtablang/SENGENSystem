import { useEffect, useState } from 'react';
import { apiFetch } from './apiClient';

/* The cache behind <Avatar>: one blob fetch per picture version, shared by every avatar on screen.
   See Avatar.jsx for why the picture is a blob rather than a plain <img src>. */

const urls = new Map();     // "userId:version" -> object URL
const pending = new Map();  // "userId:version" -> Promise

const keyOf = user => (user?.avatarVersion ? `${user.id}:${user.avatarVersion}` : null);

function forget(userId, keep) {
    for (const [key, url] of urls) {
        if (key.startsWith(`${userId}:`) && key !== keep) {
            URL.revokeObjectURL(url);
            urls.delete(key);
        }
    }
}

function load(user) {
    const key = keyOf(user);
    if (urls.has(key)) return Promise.resolve(urls.get(key));
    if (!pending.has(key)) {
        pending.set(key, apiFetch(`/api/profile/avatar?v=${encodeURIComponent(user.avatarVersion)}`, { raw: true })
            .then(r => r.blob())
            .then(blob => {
                forget(user.id, key);
                const url = URL.createObjectURL(blob);
                urls.set(key, url);
                return url;
            })
            .finally(() => pending.delete(key)));
    }
    return pending.get(key);
}

/** Seeds the cache with a picture just uploaded, so the new face shows without a round trip. */
export function primeAvatar(user, blob) {
    const key = keyOf(user);
    if (!key) return;
    forget(user.id, key);
    urls.set(key, URL.createObjectURL(blob));
}

/** Drops a user's cached picture (after they remove it). */
export function clearAvatar(user) {
    if (user?.id) forget(user.id, null);
}

export function useAvatarUrl(user) {
    const key = keyOf(user);
    const [, rerender] = useState(0);
    useEffect(() => {
        if (!key || urls.has(key)) return undefined;
        let live = true;
        load(user).then(() => { if (live) rerender(n => n + 1); }).catch(() => { /* initials stay */ });
        return () => { live = false; };
    // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [key]);
    return key ? urls.get(key) ?? null : null;
}
