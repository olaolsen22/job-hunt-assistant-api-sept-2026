using JobHunt.Api.Data;

namespace JobHunt.Api.Features.Listings;

[QueryType]
public static class ListingQueries
{
    [UsePaging]
    [UseProjection]
    [UseFiltering]
    [UseSorting]
    public static IQueryable<Listing> GetListings(AppDbContext db) =>
        db.Listings.OrderByDescending(l => l.CreatedAt).ThenByDescending(l => l.Id);

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
