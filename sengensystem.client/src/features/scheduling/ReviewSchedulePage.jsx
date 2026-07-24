import { useEffect, useState } from 'react';
import { getSchedule } from './api';
import ScheduleTable from './ScheduleTable';
import './scheduling.css';

/* FR-SCHED-06: read-only review of the current schedule, grouped by cohort with
   draft/published/override status. Manual overrides (FR-FAC-02) are a later slice —
   this page inspects; it does not yet edit. */
function ReviewSchedulePage() {
    const [rows, setRows] = useState([]);
    const [semesterName, setSemesterName] = useState('');
    const [finalized, setFinalized] = useState(false);
    const [loading, setLoading] = useState(true);
    const [alert, setAlert] = useState(null);

    useEffect(() => {
        let active = true;
        (async () => {
            try {
                const data = await getSchedule();
                if (!active) return;
                setRows(data.schedule);
                setSemesterName(data.semesterName);
                setFinalized(data.isFinalized);
            } catch (err) {
                if (active) setAlert({ kind: 'error', text: err.message });
            } finally {
                if (active) setLoading(false);
            }
        })();
        return () => { active = false; };
    }, []);

    const publishedCount = rows.filter(r => r.isPublished).length;

    return (
        <div className="sched-page">
            <header className="sched-head">
                <div>
                    <h2>Review &amp; overrides</h2>
                    <p className="sched-sub">
                        The generated timetable for
                        {semesterName ? <> <strong>{semesterName}</strong></> : ' the active semester'},
                        grouped by student block. Manual overrides arrive in an upcoming build.
                    </p>
                </div>
                {rows.length > 0 && (
                    <div className="sched-head-actions">
                        {finalized && publishedCount < rows.length && (
                            <span className="chip chip-blue">Finalized · ready to publish</span>
                        )}
                        <span className="chip chip-blue">
                            {publishedCount === rows.length ? 'All published' : `${publishedCount}/${rows.length} published`}
                        </span>
                    </div>
                )}
            </header>

            {alert && <div className="alert">{alert.text}</div>}

            {loading ? (
                <p className="sched-empty">Loading schedule…</p>
            ) : (
                <ScheduleTable rows={rows} />
            )}
        </div>
    );
}

export default ReviewSchedulePage;
