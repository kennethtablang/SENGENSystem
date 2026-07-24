// Shared file-download helpers. Centralised because the naive
// createObjectURL → a.click() → revokeObjectURL pattern has two cross-browser pitfalls that
// were repeated across several feature modules:
//   1. Firefox will not fire a programmatic click on an <a> that isn't in the document, so the
//      anchor must be appended before clicking (and removed after).
//   2. Revoking the object URL synchronously right after click() can abort or truncate the
//      download in Chromium/Safari; the revoke has to be deferred.

/** Saves a Blob to disk as `filename` via a temporary, DOM-attached anchor. */
export function saveBlob(blob, filename) {
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = filename || 'download';
    a.rel = 'noopener';
    a.style.display = 'none';
    document.body.appendChild(a);
    a.click();
    a.remove();
    // Defer revoke so the browser has started reading the blob before it's released.
    setTimeout(() => URL.revokeObjectURL(url), 10_000);
}

/** Extracts the filename from a Content-Disposition header, falling back when absent. */
export function filenameFromDisposition(disposition, fallback) {
    const match = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(disposition || '');
    return match ? decodeURIComponent(match[1].trim().replace(/^"|"$/g, '')) : fallback;
}
