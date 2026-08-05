import { useEffect, useMemo, useState } from 'react';
import { getFullSchedule, publishSchedule } from './api';
import { notifySuccess, notifyError } from '../shell/notify';
import { hhmm } from '../scheduling/calendarUtils';
import ScheduleTable from '../scheduling/ScheduleTable';
import { useTableControls } from '../shell/useTableControls';
import { SortHeader, Pagination } from '../shell/tableControls';
import '../scheduling/scheduling.css';
import './publishing.css';

/* FR-PUB: the Registrar reviews the semester's draft rows and publishes the finalized,
   constraint-verified schedule before the enrollment period opens (FR-PUB-01).
   Published rows are distributable by week, by day, and by class (FR-PUB-02); publishing
   emails affected faculty and confirmed students (FR-PUB-03). */

const dayOrder = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday'];

function FlatTable({ rows, showDay = true }) {
    // Opens in timetable order — day, then time, then block — which is how a published schedule is
    // read; any column can be taken over from there (all the rooms one faculty member is in, every
    // class in one room) without losing the default.
    const table = useTableControls(rows, {
        columns: {
            day: r => dayOrder.indexOf(r.day),
            time: r => r.startMinutes,
            cohortKey: r => r.cohortKey,
            subjectCode: r => r.subjectCode,
            sectionCode: r => r.sectionCode,
            room: r => r.room,
            faculty: r => r.faculty
        },
        initialSort: { key: 'day', dir: 'asc' },
        initialPageSize: 50
    });

    if (rows.length === 0) {
        return <p className="sched-empty">No published classes match this view yet.</p>;
    }
    return (
        <section className="card sched-group">
            <div className="sched-table-wrap">
                <table className="sched-table">
                    <thead>
                        <tr>
                            {showDay && <SortHeader label="Day" sortKey="day" sort={table.sort} onSort={table.toggleSort} />}
                            <SortHeader label="Time" sortKey="time" sort={table.sort} onSort={table.toggleSort} />
                            <SortHeader label="Block" sortKey="cohortKey" sort={table.sort} onSort={table.toggleSort} />
                            <SortHeader label="Subject" sortKey="subjectCode" sort={table.sort} onSort={table.toggleSort} />
                            <SortHeader label="Section" sortKey="sectionCode" sort={table.sort} onSort={table.toggleSort} />
                            <SortHeader label="Room" sortKey="room" sort={table.sort} onSort={table.toggleSort} />
                            <SortHeader label="Faculty" sortKey="faculty" sort={table.sort} onSort={table.toggleSort} />
                        </tr>
                    </thead>
                    <tbody>
                        {table.pageRows.map(row => (
                            <tr key={row.assignmentId}>
                                {showDay && <td>{row.day}</td>}
                                <td className="sched-mono">{hhmm(row.startMinutes)}–{hhmm(row.endMinutes)}</td>
                                <td className="sched-mono">{row.cohortKey}</td>
                                <td>
                                    <strong>{row.subjectCode}</strong>
                                    <span className="sched-subject-title">{row.subjectTitle}</span>
                                </td>
                                <td className="sched-mono">{row.sectionCode}</td>
                                <td>{row.room}</td>
                                <td>{row.faculty}</td>
                            </tr>
                        ))}
                    </tbody>
                </table>
            </div>
            <Pagination {...table} />
        </section>
    );
}

function PublishingPage() {
    const [rows, setRows] = useState([]);
    const [semesterId, setSemesterId] = useState(null);
    const [semesterName, setSemesterName] = useState('');
    const [loading, setLoading] = useState(true);
    const [publishing, setPublishing] = useState(false);
    const [confirming, setConfirming] = useState(false);
    const [alert, setAlert] = useState(null);
    const [view, setView] = useState('class'); // 'class' | 'week' | 'day'
    const [dayFilter, setDayFilter] = useState('Monday');

    async function load() {
        try {
            const data = await getFullSchedule();
            setRows(data.schedule);
            setSemesterId(data.semesterId);
            setSemesterName(data.semesterName);
        } catch (err) {
            setAlert({ kind: 'error', text: err.message });
        } finally {
            setLoading(false);
        }
    }

    useEffect(() => {
        const initial = setTimeout(load, 0);
        return () => clearTimeout(initial);
    }, []);

    const published = useMemo(() => rows.filter(r => r.isPublished), [rows]);
    const drafts = useMemo(() => rows.filter(r => !r.isPublished), [rows]);
    const daysPresent = useMemo(
        () => dayOrder.filter(d => published.some(r => r.day === d)),
        [published]);

    async function publish() {
        setConfirming(false);
        setPublishing(true);
        setAlert(null);
        try {
            const result = await publishSchedule(semesterId);
            const text = result.publishedNow > 0
                ? `Published ${result.publishedNow} class${result.publishedNow === 1 ? '' : 'es'} for ${result.semesterName}. ` +
                  `Notification emails sent to ${result.emailsSent} recipient${result.emailsSent === 1 ? '' : 's'}.`
                : 'This schedule is already fully published — nothing to do.';
            setAlert({ kind: 'success', text });
            notifySuccess(text);
            await load();
        } catch (err) {
            setAlert({ kind: 'error', text: err.message });
            notifyError(err.message);
        } finally {
            setPublishing(false);
        }
    }

    return (
        <div className="sched-page">
            <header className="sched-head">
                <div>
                    <h2>Schedule publishing</h2>
                    <p className="sched-sub">
                        Publish the finalized, conflict-verified timetable for
                        {semesterName ? <> <strong>{semesterName}</strong></> : ' the active semester'} so
                        students and faculty can see it. Publishing emails every affected person and
                        locks the rows against regeneration.
                    </p>
                </div>
                {confirming ? (
                    <div className="pub-confirm">
                        <span>Publish {drafts.length} draft class{drafts.length === 1 ? '' : 'es'} and notify everyone?</span>
                        <button className="btn btn-primary" type="button" onClick={publish}>Yes, publish</button>
                        <button className="btn" type="button" onClick={() => setConfirming(false)}>Cancel</button>
                    </div>
                ) : (
                    <button
                        className="btn btn-primary"
                        type="button"
                        onClick={() => setConfirming(true)}
                        disabled={publishing || loading || drafts.length === 0}
                    >
                        {publishing && <span className="spinner" aria-hidden="true" />}
                        {publishing ? 'Publishing…' : drafts.length === 0 ? 'Nothing to publish' : `Publish ${drafts.length} draft class${drafts.length === 1 ? '' : 'es'}`}
                    </button>
                )}
            </header>

            {alert && (
                <div className={alert.kind === 'success' ? 'alert alert-success' : 'alert'}>
                    <p>{alert.text}</p>
                </div>
            )}

            <div className="sched-stats">
                <div className="sched-stat">
                    <span className="sched-stat-num">{rows.length}</span>
                    <span className="sched-stat-label">Total classes</span>
                </div>
                <div className="sched-stat">
                    <span className="sched-stat-num">{drafts.length}</span>
                    <span className="sched-stat-label">Drafts awaiting publish</span>
                </div>
                <div className="sched-stat">
                    <span className="sched-stat-num">{published.length}</span>
                    <span className="sched-stat-label">Published</span>
                </div>
            </div>

            {loading ? (
                <p className="sched-empty">Loading schedule…</p>
            ) : rows.length === 0 ? (
                <p className="sched-empty">
                    No schedule rows for this semester yet. The Academic Head generates or builds the
                    timetable before it can be published.
                </p>
            ) : (
                <>
                    {drafts.length > 0 && (
                        <section className="pub-section">
                            <h3 className="pub-section-title">Draft rows (not yet visible to students or faculty)</h3>
                            <ScheduleTable rows={drafts} />
                        </section>
                    )}

                    <section className="pub-section">
                        <div className="pub-view-head">
                            <h3 className="pub-section-title">Published schedule</h3>
                            {published.length > 0 && (
                                <div className="pub-tabs" role="tablist">
                                    <button
                                        type="button"
                                        className={view === 'class' ? 'pub-tab pub-tab-active' : 'pub-tab'}
                                        onClick={() => setView('class')}
                                    >By class</button>
                                    <button
                                        type="button"
                                        className={view === 'week' ? 'pub-tab pub-tab-active' : 'pub-tab'}
                                        onClick={() => setView('week')}
                                    >By week</button>
                                    <button
                                        type="button"
                                        className={view === 'day' ? 'pub-tab pub-tab-active' : 'pub-tab'}
                                        onClick={() => setView('day')}
                                    >By day</button>
                                </div>
                            )}
                        </div>

                        {published.length === 0 ? (
                            <p className="sched-empty">Nothing is published yet.</p>
                        ) : view === 'class' ? (
                            <ScheduleTable rows={published} />
                        ) : view === 'week' ? (
                            <FlatTable rows={published} />
                        ) : (
                            <>
                                <div className="pub-tabs pub-day-picker">
                                    {daysPresent.map(d => (
                                        <button
                                            key={d}
                                            type="button"
                                            className={dayFilter === d ? 'pub-tab pub-tab-active' : 'pub-tab'}
                                            onClick={() => setDayFilter(d)}
                                        >{d}</button>
                                    ))}
                                </div>
                                <FlatTable rows={published.filter(r => r.day === dayFilter)} showDay={false} />
                            </>
                        )}
                    </section>
                </>
            )}
        </div>
    );
}

export default PublishingPage;
