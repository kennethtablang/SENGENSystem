import { useMemo, useState } from 'react';

/* Reusable client-side filtering + sorting + resizable pagination for every data table in the
   system. The lists are already fetched in full (the server caps at 500), so all three happen in
   the browser and the table stays responsive without another round trip.

   One hook so every table behaves the same way: click a header to sort ascending, again for
   descending, a third time to clear; type to narrow; page with a chosen page size.

   Usage:
     const t = useTableControls(rows, {
         columns: { name: r => r.fullName, units: r => r.units },  // sortable keys
         search: query,                                            // optional text filter
         searchFields: [r => r.fullName, r => r.code],             // what the text matches against
         initialSort: { key: 'name', dir: 'asc' },
         initialPageSize: 25
     });
     <TableSearch value={query} onChange={setQuery} />
     <SortHeader label="Name" sortKey="name" sort={t.sort} onSort={t.toggleSort} />
     {t.pageRows.map(...)}
     <Pagination {...t} />

   `search` is optional: a page that already filters server-side (or with status chips) simply
   passes the rows it wants and uses the hook for sorting and paging alone. */

export const PAGE_SIZES = [10, 25, 50, 100];

/* One shared empty array. The page-reset below compares list identity, and callers very reasonably
   write `data?.rows ?? []`, which hands the hook a brand-new array on every render while the data
   is still loading — enough to reset state forever. Folding every empty list onto this one constant
   makes that idiom safe. (A caller that rebuilds a NON-empty array inline each render would still
   thrash; every call site derives its rows from state, which is the contract.) */
const EMPTY = [];

export function useTableControls(rows, {
    columns = {}, initialSort = null, initialPageSize = 25, search = '', searchFields = null
} = {}) {
    const [sort, setSort] = useState(initialSort); // { key, dir: 'asc' | 'desc' } | null
    const [pageSize, setPageSize] = useState(initialPageSize);
    const [page, setPage] = useState(1);

    const source = rows?.length ? rows : EMPTY;
    const query = search.trim().toLowerCase();

    // Text filtering across whichever fields the page nominated. Values are stringified so a
    // number column ("3 units", year level) matches as readily as a name, and a null field simply
    // doesn't match rather than throwing.
    const filtered = useMemo(() => {
        if (!query || !searchFields?.length) return source;
        return source.filter(row => searchFields.some(field => {
            const value = field(row);
            return value != null && String(value).toLowerCase().includes(query);
        })) || EMPTY;
    // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [source, query]);

    // Filter/search narrows the list, and changing page size reflows the pages — either way,
    // jump back to the first page so the view never lands on an empty tail. Done during render
    // (React's "adjust state when a prop changes" pattern) rather than in an effect.
    const [seen, setSeen] = useState({ rows: filtered, pageSize });
    if (seen.rows !== filtered || seen.pageSize !== pageSize) {
        setSeen({ rows: filtered, pageSize });
        // Guarded: re-rendering to set a page that is already 1 is pure churn, and it is the one
        // thing that would turn an unstable `rows` identity into a render loop.
        if (page !== 1) setPage(1);
    }

    const sorted = useMemo(() => {
        const accessor = sort ? columns[sort.key] : null;
        if (!accessor) return filtered;
        const dir = sort.dir === 'desc' ? -1 : 1;
        return [...filtered].sort((a, b) => {
            const av = accessor(a);
            const bv = accessor(b);
            if (av == null && bv == null) return 0;
            if (av == null) return 1;
            if (bv == null) return -1;
            if (typeof av === 'number' && typeof bv === 'number') return (av - bv) * dir;
            return String(av).localeCompare(String(bv), undefined, { numeric: true, sensitivity: 'base' }) * dir;
        });
    // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [filtered, sort]);

    const total = sorted.length;
    const pageCount = Math.max(1, Math.ceil(total / pageSize));
    const current = Math.min(page, pageCount);
    const start = (current - 1) * pageSize;
    const pageRows = sorted.slice(start, start + pageSize);

    function toggleSort(key) {
        setSort(prev => {
            if (!prev || prev.key !== key) return { key, dir: 'asc' };
            if (prev.dir === 'asc') return { key, dir: 'desc' };
            return null; // third click clears the sort
        });
    }

    return {
        pageRows,
        sort,
        toggleSort,
        page: current,
        setPage,
        pageCount,
        pageSize,
        setPageSize,
        total,
        rangeStart: total === 0 ? 0 : start + 1,
        rangeEnd: Math.min(start + pageSize, total)
    };
}
