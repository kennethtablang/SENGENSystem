import { PAGE_SIZES } from './useTableControls';
import './tables.css';

/* Presentational pieces that pair with useTableControls: a search box, a sortable column header,
   and the pagination bar (page-size selector + prev/next + range label). */

/* The table filter. A plain search input, but a shared one — every table filters the same way and
   says the same thing, so the control is learned once instead of per page. */
export function TableSearch({ value, onChange, placeholder = 'Filter…', label }) {
    return (
        <label className="table-search">
            {label && <span className="table-search-label">{label}</span>}
            <input
                type="search"
                value={value}
                placeholder={placeholder}
                onChange={e => onChange(e.target.value)}
            />
        </label>
    );
}

/* One sortable header, and therefore every sortable header in the app — which is why the
   accessibility work belongs here rather than in eighteen tables.

   `aria-sort` on the `<th>` is what tells a screen-reader user which column the table is ordered by
   and in which direction. Without it the arrow glyph carries that information visually and nowhere
   else, so the table reads as an arbitrary ordering. The glyph stays `aria-hidden` because it would
   otherwise be announced as a meaningless character alongside the real state. */
export function SortHeader({ label, sortKey, sort, onSort, className }) {
    const active = sort?.key === sortKey;
    const arrow = !active ? '↕' : sort.dir === 'asc' ? '▲' : '▼';
    const ariaSort = !active ? 'none' : sort.dir === 'asc' ? 'ascending' : 'descending';

    /* What the *next* click does, so the button announces an action rather than a noun. The cycle
       is asc → desc → unsorted, matching useTableControls.toggleSort. */
    const nextAction = !active
        ? `Sort by ${label} ascending`
        : sort.dir === 'asc'
            ? `Sort by ${label} descending`
            : `Clear the sort on ${label}`;

    return (
        <th
            className={`th-sort${active ? ' is-active' : ''}${className ? ` ${className}` : ''}`}
            aria-sort={ariaSort}
        >
            <button type="button" className="th-sort-btn" onClick={() => onSort(sortKey)} title={nextAction}>
                <span>{label}</span>
                <span className="th-sort-arrow" aria-hidden="true">{arrow}</span>
                <span className="sr-only">{nextAction}</span>
            </button>
        </th>
    );
}

export function Pagination({
    page, pageCount, pageSize, setPage, setPageSize, total, rangeStart, rangeEnd, sizes = PAGE_SIZES
}) {
    return (
        <div className="table-pager">
            <label className="table-pager-size">
                <span>Rows per page</span>
                <select value={pageSize} onChange={e => setPageSize(Number(e.target.value))}>
                    {sizes.map(s => <option key={s} value={s}>{s}</option>)}
                </select>
            </label>
            {/* Polite live region: paging replaces the table's contents with no other signal, so
                without this a screen-reader user clicks Next and hears nothing at all. Announced
                after the fact rather than interrupting, since they asked for it. */}
            <div className="table-pager-range" aria-live="polite">
                {total === 0 ? 'No rows' : `${rangeStart}–${rangeEnd} of ${total}`}
            </div>
            <div className="table-pager-nav">
                <button type="button" className="btn btn-ghost btn-sm"
                    disabled={page <= 1} onClick={() => setPage(page - 1)}>
                    Prev<span className="sr-only"> page</span>
                </button>
                <span className="table-pager-page">Page {page} / {pageCount}</span>
                <button type="button" className="btn btn-ghost btn-sm"
                    disabled={page >= pageCount} onClick={() => setPage(page + 1)}>
                    Next<span className="sr-only"> page</span>
                </button>
            </div>
        </div>
    );
}
