/* Renders schedule rows grouped by student cohort (block). Rows arrive already
   ordered by cohort → day → start time from the server. Shared by the generate
   and review pages (FR-SCHED-06).

   Filtering and sorting, but deliberately no pagination: the grouping IS the structure here — one
   card per block, each holding that block's whole week — and paging would cut a timetable in half.
   The filter narrows across every block at once (find a room, a subject, a faculty member), and a
   column sort applies inside each block so the comparison stays block-by-block. */

import { useMemo, useState } from 'react';
import { hhmm } from './calendarUtils';
import { SortHeader, TableSearch } from '../shell/tableControls';

const dayOrder = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday'];

const columns = {
    subjectCode: r => r.subjectCode,
    component: r => r.component,
    sectionCode: r => r.sectionCode,
    day: r => dayOrder.indexOf(r.day),
    time: r => r.startMinutes,
    room: r => r.room,
    faculty: r => r.faculty,
    status: r => (r.isManualOverride ? 0 : r.isPublished ? 1 : 2)
};

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
    const [search, setSearch] = useState('');
    const [sort, setSort] = useState({ key: 'day', dir: 'asc' });

    const visible = useMemo(() => {
        const q = search.trim().toLowerCase();
        if (!q || !rows) return rows ?? [];
        return rows.filter(r =>
            [r.subjectCode, r.subjectTitle, r.sectionCode, r.cohortKey, r.day, r.room, r.faculty]
                .some(v => v && String(v).toLowerCase().includes(q)));
    }, [rows, search]);

    const groups = useMemo(() => {
        const accessor = sort ? columns[sort.key] : null;
        const dir = sort?.dir === 'desc' ? -1 : 1;
        const sorter = (a, b) => {
            // No sort chosen (or an unknown key): timetable order, the way a week is read.
            if (!accessor) {
                return (dayOrder.indexOf(a.day) - dayOrder.indexOf(b.day)) || (a.startMinutes - b.startMinutes);
            }
            const av = accessor(a);
            const bv = accessor(b);
            // Ties fall back to the timetable's own order rather than to however the array arrived.
            if (typeof av === 'number' && typeof bv === 'number') {
                return ((av - bv) * dir) || (a.startMinutes - b.startMinutes);
            }
            return (String(av ?? '').localeCompare(String(bv ?? ''), undefined, { numeric: true }) * dir)
                || (a.startMinutes - b.startMinutes);
        };
        return groupByCohort(visible).map(g => ({ ...g, items: [...g.items].sort(sorter) }));
    }, [visible, sort]);

    function toggleSort(key) {
        setSort(prev => {
            if (!prev || prev.key !== key) return { key, dir: 'asc' };
            if (prev.dir === 'asc') return { key, dir: 'desc' };
            return null; // third click restores the timetable's own order
        });
    }

    if (!rows || rows.length === 0) {
        return <p className="sched-empty">No schedule rows for this semester yet.</p>;
    }

    const header = (label, key) => (
        <SortHeader label={label} sortKey={key} sort={sort} onSort={toggleSort} />
    );

    return (
        <>
            <div className="table-toolbar">
                <TableSearch
                    value={search} onChange={setSearch}
                    placeholder="Filter subject, room, block, or faculty…"
                />
                <span className="table-toolbar-spacer" />
                <span className="sched-filter-count">
                    {visible.length === rows.length
                        ? `${rows.length} class meetings`
                        : `${visible.length} of ${rows.length} class meetings`}
                </span>
            </div>

            {groups.length === 0 ? (
                <p className="sched-empty">No classes match your filter.</p>
            ) : (
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
                                            {header('Subject', 'subjectCode')}
                                            {header('Meeting', 'component')}
                                            {header('Section', 'sectionCode')}
                                            {header('Day', 'day')}
                                            {header('Time', 'time')}
                                            {header('Room', 'room')}
                                            {header('Faculty', 'faculty')}
                                            {header('Status', 'status')}
                                        </tr>
                                    </thead>
                                    <tbody>
                                        {items.map(row => (
                                            <tr key={row.assignmentId}>
                                                <td>
                                                    <strong>{row.subjectCode}</strong>
                                                    <span className="sched-subject-title">{row.subjectTitle}</span>
                                                </td>
                                                <td>
                                                    {/* A lecture–laboratory subject appears twice —
                                                        once per meeting, in different rooms. */}
                                                    <span className={`chip ${row.component === 'Laboratory' ? 'chip-lab' : 'chip-muted'}`}>
                                                        {row.component === 'Laboratory' ? 'Lab' : 'Lec'}
                                                    </span>
                                                </td>
                                                <td className="sched-mono">{row.sectionCode}</td>
                                                <td>{row.day}</td>
                                                <td className="sched-mono">{hhmm(row.startMinutes)}–{hhmm(row.endMinutes)}</td>
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
            )}
        </>
    );
}

export default ScheduleTable;
