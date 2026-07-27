import { PAGE_SIZES } from './useTableControls';

/* Presentational pieces that pair with useTableControls: a sortable column header and the
   pagination bar (page-size selector + prev/next + range label). */

export function SortHeader({ label, sortKey, sort, onSort, className }) {
    const active = sort?.key === sortKey;
    const arrow = !active ? '↕' : sort.dir === 'asc' ? '▲' : '▼';
    return (
        <th className={`th-sort${active ? ' is-active' : ''}${className ? ` ${className}` : ''}`}>
            <button type="button" className="th-sort-btn" onClick={() => onSort(sortKey)}>
                <span>{label}</span>
                <span className="th-sort-arrow" aria-hidden="true">{arrow}</span>
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
            <div className="table-pager-range">
                {total === 0 ? 'No rows' : `${rangeStart}–${rangeEnd} of ${total}`}
            </div>
            <div className="table-pager-nav">
                <button type="button" className="btn btn-ghost btn-sm"
                    disabled={page <= 1} onClick={() => setPage(page - 1)}>
                    Prev
                </button>
                <span className="table-pager-page">Page {page} / {pageCount}</span>
                <button type="button" className="btn btn-ghost btn-sm"
                    disabled={page >= pageCount} onClick={() => setPage(page + 1)}>
                    Next
                </button>
            </div>
        </div>
    );
}
