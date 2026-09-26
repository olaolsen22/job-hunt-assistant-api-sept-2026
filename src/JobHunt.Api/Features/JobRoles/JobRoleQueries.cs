using HotChocolate.Data.Sorting;
using JobHunt.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace JobHunt.Api.Features.JobRoles;

[QueryType]
public static class JobRoleQueries
{
    // search matches NormalizedName because the built-in `contains` filter is case-sensitive on SQLite.
    [UsePaging]
    [UseProjection]
    [UseFiltering]
    [UseSorting]
    public static IQueryable<JobRole> GetJobRoles(string? search, AppDbContext db, ISortingContext sorting)
    {
        // Default order only without a client `order`: Hot Chocolate adds that as ThenBy, so this would win.
        // Handled(false): taking ISortingContext turns off automatic sorting otherwise.
        sorting.Handled(false);
        IQueryable<JobRole> jobRoles = sorting.IsDefined ? db.JobRoles : db.JobRoles.OrderBy(r => r.Name);
        if (search is null) return jobRoles;
        var name = Names.Normalize(search);
        return jobRoles.Where(r => r.NormalizedName.Contains(name));
    }
}

[ObjectType<JobRole>]
public static partial class JobRoleNode
{
    static partial void Configure(IObjectTypeDescriptor<JobRole> descriptor)
    {
        descriptor.Ignore(r => r.UserId);
        descriptor.Ignore(r => r.NormalizedName);
        // listingCount needs the Id even when the client didn't select it.
        descriptor.Field(r => r.Id).IsProjected();
    }

    public static async Task<int> GetListingCountAsync(
        [Parent] JobRole jobRole, IListingCountByJobRoleIdDataLoader listingCounts, CancellationToken ct) =>
        await listingCounts.LoadAsync(jobRole.Id, ct);
}

internal static class JobRoleDataLoaders
{
    [DataLoader]
    public static async Task<Dictionary<int, int>> GetListingCountByJobRoleIdAsync(
        IReadOnlyList<int> jobRoleIds, AppDbContext db, CancellationToken ct) =>
        await db.Listings
            .Where(l => jobRoleIds.Contains(l.JobRoleId))
            .GroupBy(l => l.JobRoleId)
            .ToDictionaryAsync(g => g.Key, g => g.Count(), ct);
}
