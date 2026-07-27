import { useMemo, useState } from 'react';

/* Reusable client-side sorting + resizable pagination for the staff tables (Document
   Requirements, Term Activations, Pre-authorization, Registration). The lists are already
   fetched in full (server caps at 500), so sort and paging happen in the browser.

   Usage:
     const t = useTableControls(rows, { columns: { name: r => r.fullName, ... }, initialPageSize: 25 });
     <SortHeader label="Name" sortKey="name" sort={t.sort} onSort={t.toggleSort} />
     {t.pageRows.map(...)}
     <Pagination {...t} /> */

export const PAGE_SIZES = [10, 25, 50, 100];

export function useTableControls(rows, { columns = {}, initialSort = null, initialPageSize = 25 } = {}) {
    const [sort, setSort] = useState(initialSort); // { key, dir: 'asc' | 'desc' } | null
    const [pageSize, setPageSize] = useState(initialPageSize);
    const [page, setPage] = useState(1);

    // Filter/search hand us a new array, and changing page size reflows the pages — either way,
    // jump back to the first page so the view never lands on an empty tail. Done during render
    // (React's "adjust state when a prop changes" pattern) rather than in an effect.
    const [seen, setSeen] = useState({ rows, pageSize });
    if (seen.rows !== rows || seen.pageSize !== pageSize) {
        setSeen({ rows, pageSize });
        setPage(1);
    }

    const sorted = useMemo(() => {
        const accessor = sort ? columns[sort.key] : null;
        if (!accessor) return rows;
        const dir = sort.dir === 'desc' ? -1 : 1;
        return [...rows].sort((a, b) => {
            const av = accessor(a);
            const bv = accessor(b);
            if (av == null && bv == null) return 0;
            if (av == null) return 1;
            if (bv == null) return -1;
            if (typeof av === 'number' && typeof bv === 'number') return (av - bv) * dir;
            return String(av).localeCompare(String(bv), undefined, { numeric: true, sensitivity: 'base' }) * dir;
        });
    // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [rows, sort]);

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
