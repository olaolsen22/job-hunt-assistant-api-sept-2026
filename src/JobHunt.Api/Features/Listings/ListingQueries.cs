using HotChocolate.Data.Sorting;
using JobHunt.Api.Data;

namespace JobHunt.Api.Features.Listings;

[QueryType]
public static class ListingQueries
{
    [UsePaging]
    [UseProjection]
    [UseFiltering]
    [UseSorting]
    public static IQueryable<Listing> GetListings(AppDbContext db, ISortingContext sorting)
    {
        // Hot Chocolate adds a client's `order` as ThenBy, so a fixed OrderBy here would always win.
        // Newest first only without one; with one, Id breaks ties so equal values (e.g. no deadline)
        // don't shuffle between pages. Handled(false): taking ISortingContext turns off automatic sorting otherwise.
        sorting.Handled(false);
        sorting.OnAfterSortingApplied<IQueryable<Listing>>(static (sortingApplied, query) =>
            sortingApplied && query is IOrderedQueryable<Listing> ordered
                ? ordered.ThenByDescending(l => l.Id)
                : query.OrderByDescending(l => l.CreatedAt).ThenByDescending(l => l.Id));
        return db.Listings;
    }

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
