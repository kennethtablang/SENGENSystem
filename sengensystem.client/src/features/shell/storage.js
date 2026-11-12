/* localStorage that cannot take the app down.

   Storage can be unavailable — a private window, site data blocked by policy (common on managed
   school machines), a full quota — and then every access throws. The app read the sidebar state
   during render and the sign-in token on every request, so a browser that blocks storage got a
   crashed shell on every page rather than an app that simply forgets things between visits.

   Every read and write goes through here instead. When the real store throws, values are kept in
   memory for this tab, so signing in still works for the visit; they just do not survive a reload. */

const memory = new Map();

export function readItem(key) {
    try {
        return localStorage.getItem(key);
    } catch {
        return memory.has(key) ? memory.get(key) : null;
    }
}

export function writeItem(key, value) {
    try {
        localStorage.setItem(key, value);
    } catch {
        memory.set(key, value);
    }
}

export function removeItem(key) {
    try {
        localStorage.removeItem(key);
    } catch {
        // fall through to the in-memory copy
    }
    memory.delete(key);
}
