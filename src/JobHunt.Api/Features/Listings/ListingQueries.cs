using HotChocolate.Data.Sorting;
using JobHunt.Api.Data;

namespace JobHunt.Api.Features.Listings;

[QueryType]
public static class ListingQueries
{
    // ASAP ads have no date but still close; treat them as due this long after posting.
    private const int AsapDays = 14;

    // Offset paging (skip/take), not cursors: the UI shows numbered pages and jumps straight to page N.
    [UseOffsetPaging(DefaultPageSize = 20, MaxPageSize = 100, IncludeTotalCount = true)]
    [UseProjection]
    [UseFiltering]
    [UseSorting]
    public static IQueryable<Listing> GetListings(AppDbContext db, ISortingContext sorting, TimeProvider time)
    {
        // Hot Chocolate adds a client's `order` as ThenBy, so a fixed OrderBy here would always win.
        // Default order only without one; with one, Id breaks ties so equal values (e.g. no deadline)
        // don't shuffle between pages. Handled(false): taking ISortingContext turns off automatic sorting otherwise.
        var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        sorting.Handled(false);
        sorting.OnAfterSortingApplied<IQueryable<Listing>>((sortingApplied, query) =>
            sortingApplied && query is IOrderedQueryable<Listing> ordered
                ? ordered.ThenByDescending(l => l.Id)
                : ByClosestDeadline(query, today));
        return db.Listings;
    }

    // Upcoming deadlines (dated or ASAP) soonest first, then undated, then expired.
    private static IQueryable<Listing> ByClosestDeadline(IQueryable<Listing> query, DateOnly today) =>
        query
            .Select(l => new
            {
                Listing = l,
                Due = l.DeadlineType == DeadlineType.Asap ? l.PostedOn.AddDays(AsapDays) : l.DeadlineOn,
            })
            .OrderBy(x => x.Due == null ? 1 : x.Due >= today ? 0 : 2)
            .ThenBy(x => x.Due)
            .ThenByDescending(x => x.Listing.CreatedAt)
            .ThenByDescending(x => x.Listing.Id)
            .Select(x => x.Listing);

    [UseFirstOrDefault]
    [UseProjection]
    public static IQueryable<Listing> GetListingById(int id, AppDbContext db) =>
        db.Listings.Where(l => l.Id == id);
}

[ObjectType<Listing>]
public static partial class ListingNode
{
    static partial void Configure(IObjectTypeDescriptor<Listing> descriptor)
    {
        descriptor.Ignore(l => l.UserId);
    }
}
