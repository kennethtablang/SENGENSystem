import { toast } from 'react-toastify';
import { toastAutoClose, toastPosition } from '../settings/prefs';

// One voice for every CRUD outcome in SEN-GEN (FR-wide UX): call notifySuccess after a
// create/update/delete commits, notifyError from the catch. Pages may still render inline
// alerts for detail (validation lists, blockers) — the toast is the at-a-glance signal.
// Lifetime and corner follow the Settings-page notification preferences.

const base = () => ({
    position: toastPosition(),
    autoClose: toastAutoClose(),
    hideProgressBar: false,
    closeOnClick: true,
    pauseOnHover: true,
    draggable: false
});

export const notifySuccess = (message) => toast.success(message, base());

export const notifyError = (message) =>
    toast.error(message || 'Something went wrong. Please try again.', { ...base(), autoClose: toastAutoClose() + 1500 });

export const notifyInfo = (message) => toast.info(message, base());
