using HotChocolate.Data.Sorting;
using JobHunt.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace JobHunt.Api.Features.Locations;

[QueryType]
public static class LocationQueries
{
    // search matches the city only; the UI matches country names itself via Intl.DisplayNames.
    [UsePaging]
    [UseProjection]
    [UseFiltering]
    [UseSorting]
    public static IQueryable<Location> GetLocations(string? search, AppDbContext db, ISortingContext sorting)
    {
        // Default order only without a client `order`, which Hot Chocolate adds as ThenBy.
        sorting.Handled(false);
        IQueryable<Location> locations = sorting.IsDefined
            ? db.Locations
            : db.Locations.OrderBy(l => l.CountryCode).ThenBy(l => l.City);
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
