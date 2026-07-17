import { useEffect, useMemo, useState } from 'react';
import FullCalendar from '@fullcalendar/react';
import timeGridPlugin from '@fullcalendar/timegrid';
import { getMySchedule } from './api';
import { REF_DATES, toIso, hhmm, fmtHours, subjectColor, slotLabelFormat } from './calendarUtils';
import './board.css';
import './myschedule.css';

const DAY_NAMES = ['', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];

// FR-FAC-05: the signed-in user views their own finalized weekly timetable (read-only).
export default function SchedulePage() {
    const [data, setData] = useState(null);
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState(null);

    useEffect(() => {
        let active = true;
        (async () => {
            setLoading(true); setError(null);
            try {
                const res = await getMySchedule();
                if (active) setData(res);
            } catch (err) {
                if (active) setError(err.message);
            } finally {
                if (active) setLoading(false);
            }
        })();
        return () => { active = false; };
    }, []);

    const entries = data?.entries ?? [];
    const isStudent = data?.role === 'Student';

    const events = useMemo(() => entries.map(e => {
        const c = subjectColor(e.subjectId);
        return {
            id: e.assignmentId,
            start: toIso(e.day, e.startMinutes),
            end: toIso(e.day, e.endMinutes),
            backgroundColor: c.bg,
            borderColor: c.border,
            textColor: c.text,
            extendedProps: e
        };
    }), [entries]);

    // Group the timetable by weekday for the readable day-by-day list (FR-PUB-02).
    const byDay = useMemo(() => {
        const groups = [];
        for (let d = 1; d <= 6; d++) {
            const list = entries.filter(e => e.day === d).sort((a, b) => a.startMinutes - b.startMinutes);
            if (list.length) groups.push({ day: d, list });
        }
        return groups;
    }, [entries]);

    return (
        <div className="myx-page">
            <header className="myx-head">
                <div>
                    <h2>My schedule</h2>
                    <p className="myx-sub">
                        Your weekly timetable for{' '}
                        {data?.semesterName ? <strong>{data.semesterName}</strong> : 'the active semester'}.
                    </p>
                </div>
                {entries.length > 0 && (
                    <div className="myx-summary">
                        <span className="chip chip-blue">{data.count} {data.count === 1 ? 'class' : 'classes'}</span>
                        <span className="chip chip-blue">{fmtHours(data.totalHours)} h / week</span>
                        {!data.isPublished && <span className="chip chip-draft">Preview · not yet published</span>}
                    </div>
                )}
            </header>

            {error && <div className="alert">{error}</div>}

            {loading ? (
                <p className="myx-empty">Loading your schedule…</p>
            ) : entries.length === 0 ? (
                <div className="myx-emptycard card">
                    <div className={`myx-empty-mark${isStudent ? ' is-student' : ''}`} aria-hidden>
                        <svg viewBox="0 0 24 24" width="34" height="34" fill="none" stroke="currentColor"
                            strokeWidth="1.6" strokeLinecap="round" strokeLinejoin="round">
                            <path d="M3 5h18v16H3zM3 9.5h18M8 3v4M16 3v4" />
                        </svg>
                    </div>
                    <p className="myx-empty-title">No classes to show yet</p>
                    <p className="myx-empty-text">{data?.message || 'Your schedule will appear here once it is available.'}</p>
                </div>
            ) : (
                <div className="myx-grid">
                    <div className="board-cal card">
                        <FullCalendar
                            plugins={[timeGridPlugin]}
                            initialView="timeGridWeek"
                            initialDate={REF_DATES[0]}
                            headerToolbar={false}
                            hiddenDays={[0]}
                            allDaySlot={false}
                            dayHeaderFormat={{ weekday: 'long' }}
                            slotMinTime="07:00:00"
                            slotMaxTime="18:00:00"
                            slotDuration="00:30:00"
                            slotLabelFormat={slotLabelFormat()}
                            expandRows
                            height={640}
                            editable={false}
                            selectable={false}
                            events={events}
                            eventContent={(arg) => {
                                const e = arg.event.extendedProps;
                                return (
                                    <div className="ev">
                                        <span className="ev-code">{e.subjectCode}</span>
                                        <span className="ev-meta">{e.room}</span>
                                        <span className="ev-meta">{isStudent ? e.facultyName : e.cohortLabel}</span>
                                    </div>
                                );
                            }}
                        />
                    </div>

                    <aside className="myx-list">
                        {byDay.map(({ day, list }) => (
                            <section key={day} className="myx-day card">
                                <h3 className="myx-day-title">{DAY_NAMES[day]}</h3>
                                <ul className="myx-day-list">
                                    {list.map(e => (
                                        <li key={e.assignmentId} className="myx-item" style={{ borderLeftColor: subjectColor(e.subjectId).border }}>
                                            <div className="myx-item-time">{hhmm(e.startMinutes)}–{hhmm(e.endMinutes)}</div>
                                            <div className="myx-item-main">
                                                <span className="myx-item-code">{e.subjectCode}</span>
                                                <span className="myx-item-title">{e.subjectTitle}</span>
                                            </div>
                                            <div className="myx-item-meta">
                                                <span>{e.cohortLabel}</span>
                                                <span aria-hidden>·</span>
                                                <span>{e.room}</span>
                                                <span aria-hidden>·</span>
                                                <span className="myx-item-seats">{e.enrolled}/{e.capacity} seats</span>
                                            </div>
                                        </li>
                                    ))}
                                </ul>
                            </section>
                        ))}
                    </aside>
                </div>
            )}
        </div>
    );
}
