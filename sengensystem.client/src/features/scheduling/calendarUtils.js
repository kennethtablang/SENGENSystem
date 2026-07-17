// Shared helpers for the scheduling calendars (Schedule Board + personal My Schedule) so a
// subject reads as the same colour everywhere and weekday/time mapping stays consistent.

import { uses12HourTime } from '../settings/prefs';

// Mon–Sat of an arbitrary reference week (a Monday). Schedules are weekday + time of day, not
// real dates, so every event lives in this fixed week; the calendars hide the dates themselves.
export const REF_DATES = ['2024-01-01', '2024-01-02', '2024-01-03', '2024-01-04', '2024-01-05', '2024-01-06'];

export function toIso(day, minutes) {
    const hh = String(Math.floor(minutes / 60)).padStart(2, '0');
    const mm = String(minutes % 60).padStart(2, '0');
    return `${REF_DATES[day - 1]}T${hh}:${mm}:00`;
}

export function fromDate(date) {
    // Jan 1–6 2024 map to weekday ints 1–6 (Mon–Sat); time of day → minutes.
    return { day: date.getDate(), minutes: date.getHours() * 60 + date.getMinutes() };
}

export function hhmm(minutes) {
    const h = Math.floor(minutes / 60);
    const mm = String(minutes % 60).padStart(2, '0');
    if (uses12HourTime()) return `${h % 12 || 12}:${mm} ${h < 12 ? 'AM' : 'PM'}`;
    return `${String(h).padStart(2, '0')}:${mm}`;
}

// FullCalendar axis labels follow the same Settings preference as hhmm().
export function slotLabelFormat() {
    return uses12HourTime()
        ? { hour: 'numeric', minute: '2-digit', hour12: true }
        : { hour: '2-digit', minute: '2-digit', hour12: false };
}

export function fmtHours(n) {
    return Number.isInteger(n) ? String(n) : n.toFixed(1);
}

// Each subject gets its own stable colour so blocks read apart and the same subject looks
// identical across the pool, the calendar, and the trackers. Derived from the subject id (no
// stored field) against a curated, well-spaced hue set. Theme-aware: light tint + dark-blue
// text on the light theme; deep tint + light text on the dark theme (data-theme on <html>).
const SUBJECT_HUES = [214, 265, 330, 24, 43, 158, 190, 288, 8, 128, 300, 174];

export function subjectColor(id) {
    let h = 0;
    for (let i = 0; i < id.length; i++) h = (h * 31 + id.charCodeAt(i)) >>> 0;
    const hue = SUBJECT_HUES[h % SUBJECT_HUES.length];
    if (document.documentElement.dataset.theme === 'dark') {
        return {
            bg: `hsl(${hue} 42% 21%)`,
            border: `hsl(${hue} 65% 58%)`,
            text: '#e9efff'
        };
    }
    return {
        bg: `hsl(${hue} 72% 95%)`,
        border: `hsl(${hue} 60% 50%)`,
        text: '#0e2a66'
    };
}
