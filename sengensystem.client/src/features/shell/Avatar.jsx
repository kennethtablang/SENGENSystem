import { useAvatarUrl } from './avatarCache';

/* The signed-in user's profile picture, or their initials when they have none.

   The picture is behind the bearer token, so it cannot be a plain <img src="/api/…">: it is fetched
   once as a blob and shown from an object URL. One fetch per picture version, shared by every
   avatar on screen (top bar, menu, profile page) — the version comes with the user record and
   changes whenever the picture does, so a cached copy is never stale. */

export default function Avatar({ user, className = '' }) {
    const url = useAvatarUrl(user);
    const initials = `${user?.firstName?.[0] ?? ''}${user?.lastName?.[0] ?? ''}`.toUpperCase();
    return (
        <span className={`avatar${url ? ' has-photo' : ''}${className ? ` ${className}` : ''}`}>
            {/* Decorative: the name is always written beside it. */}
            {url ? <img src={url} alt="" draggable="false" /> : initials}
        </span>
    );
}
