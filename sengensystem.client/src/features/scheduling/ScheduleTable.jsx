/* Renders schedule rows grouped by student cohort (block). Rows arrive already
   ordered by cohort → day → start time from the server. Shared by the generate
   and review pages (FR-SCHED-06). */

const dayOrder = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday'];

function groupByCohort(rows) {
    const map = new Map();
    for (const row of rows) {
        if (!map.has(row.cohortKey)) map.set(row.cohortKey, []);
        map.get(row.cohortKey).push(row);
    }
    return [...map.entries()].map(([cohort, items]) => ({ cohort, items }));
}

function StatusBadge({ row }) {
    if (row.isManualOverride) return <span className="chip chip-yellow">Override</span>;
    if (row.isPublished) return <span className="chip chip-blue">Published</span>;
    return <span className="chip chip-muted">Draft</span>;
}

function ScheduleTable({ rows }) {
    if (!rows || rows.length === 0) {
        return <p className="sched-empty">No schedule rows for this semester yet.</p>;
    }

    const groups = groupByCohort(rows);

    return (
        <div className="sched-groups">
            {groups.map(({ cohort, items }) => (
                <section className="card sched-group" key={cohort}>
                    <header className="sched-group-head">
                        <h3>Block {cohort}</h3>
                        <span className="chip chip-muted">{items.length} classes</span>
                    </header>
                    <div className="sched-table-wrap">
                        <table className="sched-table">
                            <thead>
                                <tr>
                                    <th>Subject</th>
                                    <th>Section</th>
                                    <th>Day</th>
                                    <th>Time</th>
                                    <th>Room</th>
                                    <th>Faculty</th>
                                    <th>Status</th>
                                </tr>
                            </thead>
                            <tbody>
                                {items
                                    .slice()
                                    .sort((a, b) =>
                                        (dayOrder.indexOf(a.day) - dayOrder.indexOf(b.day)) ||
                                        a.time.localeCompare(b.time))
                                    .map(row => (
                                        <tr key={row.assignmentId}>
                                            <td>
                                                <strong>{row.subjectCode}</strong>
                                                <span className="sched-subject-title">{row.subjectTitle}</span>
                                            </td>
                                            <td className="sched-mono">{row.sectionCode}</td>
                                            <td>{row.day}</td>
                                            <td className="sched-mono">{row.time}</td>
                                            <td>{row.room}</td>
                                            <td>{row.faculty}</td>
                                            <td><StatusBadge row={row} /></td>
                                        </tr>
                                    ))}
                            </tbody>
                        </table>
                    </div>
                </section>
            ))}
        </div>
    );
}

export default ScheduleTable;
