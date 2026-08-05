using Microsoft.EntityFrameworkCore;

namespace SENGENSystem.Server.Common.Paging
{
    /// <summary>
    /// One page of a list, and how big the list actually is.
    ///
    /// <para>
    /// The queues used to answer with <c>.Take(500)</c> and a <c>count</c> of what came back, which
    /// made a truncated list indistinguishable from a complete one. Worse, the client filtered and
    /// searched only the rows it had received, so past row 500 a record did not appear on a later
    /// page — it did not exist as far as the UI was concerned, and the search box would report "no
    /// results" for a student who is in the database.
    /// </para>
    ///
    /// <para>
    /// <see cref="Total"/> is therefore the point of this type: it is the count <i>before</i>
    /// paging, so the page can always say "showing 25 of 1,340" and the pager knows how far it can
    /// go. Every list endpoint returns it.
    /// </para>
    /// </summary>
    public sealed record Paged<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize)
    {
        public int PageCount => Total <= 0 ? 1 : (int)Math.Ceiling(Total / (double)PageSize);

        /// <summary>Project the rows to their DTOs while keeping the paging envelope intact.</summary>
        public Paged<TOut> Select<TOut>(Func<T, TOut> map) =>
            new(Items.Select(map).ToList(), Total, Page, PageSize);

        /// <summary>
        /// The response body every list endpoint returns. <c>count</c> keeps its original meaning
        /// (rows in this response) so existing callers are unaffected; everything else is additive.
        ///
        /// <para>Returns the dictionary rather than <c>object</c> so an endpoint with its own
        /// summary figures can add them — <c>body["pendingCount"] = …</c> — without casting. Those
        /// figures must be counted in SQL over the filtered query, never over <see cref="Items"/>,
        /// which is now one page rather than the whole list.</para>
        /// </summary>
        public Dictionary<string, object?> ToResponse(string itemsKey) => new()
        {
            ["count"] = Items.Count,
            ["total"] = Total,
            ["page"] = Page,
            ["pageSize"] = PageSize,
            ["pageCount"] = PageCount,
            [itemsKey] = Items
        };
    }

    /// <summary>Which page was asked for, normalized into something safe to hand to SQL.</summary>
    public sealed record PageSpec(int Page, int PageSize)
    {
        public int Skip => (Page - 1) * PageSize;

        public const int DefaultPageSize = 25;

        /// <summary>
        /// The ceiling on a single page. Generous enough that an export-minded user can pull a big
        /// slice in one request, low enough that a hand-edited <c>?pageSize=100000</c> cannot ask
        /// the database to materialize the entire table.
        /// </summary>
        public const int MaxPageSize = 200;

        /// <summary>
        /// Clamp whatever arrived on the query string. Absent, zero, negative, and absurd values all
        /// resolve to something sane rather than being rejected — a paging parameter is not worth
        /// failing a request over.
        /// </summary>
        public static PageSpec From(int? page, int? pageSize, int defaultPageSize = DefaultPageSize) =>
            new(Math.Max(1, page ?? 1),
                Math.Clamp(pageSize ?? defaultPageSize, 1, MaxPageSize));
    }

    public static class PagingExtensions
    {
        /// <summary>
        /// Count the filtered query, then fetch just the requested window.
        ///
        /// <para>Two round trips by design: the count has to be taken against the same filters but
        /// without the paging, and doing it in SQL is the whole point — the alternative is loading
        /// every row to count them, which is what this replaces.</para>
        ///
        /// <para>Requires an ordered query. SQL Server has no stable <c>OFFSET</c> without an
        /// <c>ORDER BY</c>, so paging an unordered query silently repeats and drops rows between
        /// pages; taking <see cref="IOrderedQueryable{T}"/> makes that a compile error instead of a
        /// bug someone finds on page 3.</para>
        /// </summary>
        public static async Task<Paged<T>> ToPagedAsync<T>(
            this IOrderedQueryable<T> query, PageSpec spec, CancellationToken cancellationToken)
        {
            var total = await query.CountAsync(cancellationToken);

            // Asking for page 9 of a list that just shrank to 3 pages should show the last page,
            // not an empty table with no way back.
            var pageCount = total <= 0 ? 1 : (int)Math.Ceiling(total / (double)spec.PageSize);
            var page = Math.Min(spec.Page, pageCount);
            var skip = (page - 1) * spec.PageSize;

            var items = await query.Skip(skip).Take(spec.PageSize).ToListAsync(cancellationToken);
            return new Paged<T>(items, total, page, spec.PageSize);
        }
    }
}
