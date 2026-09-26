using JobHunt.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace JobHunt.Api.Features.Locations;

[QueryType]
public static class LocationQueries
{
    // search matches the city only; country names aren't stored, so the UI matches those
    // itself via Intl.DisplayNames (a user has few enough locations to load them all).
    [UsePaging]
    [UseProjection]
    [UseFiltering]
    [UseSorting]
    public static IQueryable<Location> GetLocations(string? search, AppDbContext db)
    {
        var locations = db.Locations.OrderBy(l => l.CountryCode).ThenBy(l => l.City).AsQueryable();
        if (search is null) return locations;
        var city = Names.Normalize(search);
        return locations.Where(l => l.NormalizedCity.Contains(city));
    }
}

[ObjectType<Location>]
public static partial class LocationNode
{
    static partial void Configure(IObjectTypeDescriptor<Location> descriptor)
    {
        descriptor.Ignore(l => l.UserId);
        descriptor.Ignore(l => l.NormalizedCity);
        // listingCount needs the Id even when the client didn't select it.
        descriptor.Field(l => l.Id).IsProjected();
    }

    public static async Task<int> GetListingCountAsync(
        [Parent] Location location, IListingCountByLocationIdDataLoader listingCounts, CancellationToken ct) =>
        await listingCounts.LoadAsync(location.Id, ct);
}

internal static class LocationDataLoaders
{
    [DataLoader]
    public static async Task<Dictionary<int, int>> GetListingCountByLocationIdAsync(
        IReadOnlyList<int> locationIds, AppDbContext db, CancellationToken ct) =>
        await db.ListingLocations
            .Where(l => locationIds.Contains(l.LocationId))
            .GroupBy(l => l.LocationId)
            .ToDictionaryAsync(g => g.Key, g => g.Count(), ct);
}
