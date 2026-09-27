using HotChocolate.Data.Sorting;
using JobHunt.Api.Data;
using JobHunt.Api.Features.Applications;
using Microsoft.EntityFrameworkCore;

namespace JobHunt.Api.Features.Companies;

[QueryType]
public static class CompanyQueries
{
    // search matches NormalizedName because the built-in `contains` filter is case-sensitive on SQLite.
    [UsePaging]
    [UseProjection]
    [UseFiltering]
    [UseSorting]
    public static IQueryable<Company> GetCompanies(string? search, AppDbContext db, ISortingContext sorting)
    {
        // Default order only without a client `order`: Hot Chocolate adds that as ThenBy, so this would win.
        // Handled(false): taking ISortingContext turns off automatic sorting otherwise.
        sorting.Handled(false);
        IQueryable<Company> companies = sorting.IsDefined ? db.Companies : db.Companies.OrderBy(c => c.Name);
        if (search is null) return companies;
        var name = Names.Normalize(search);
        return companies.Where(c => c.NormalizedName.Contains(name));
    }
}

[ObjectType<Company>]
public static partial class CompanyNode
{
    static partial void Configure(IObjectTypeDescriptor<Company> descriptor)
    {
        descriptor.Ignore(c => c.UserId);
        descriptor.Ignore(c => c.NormalizedName);
        // The counts need the Id even when the client didn't select it.
        descriptor.Field(c => c.Id).IsProjected();
    }

    // Computed, never stored, so it can't drift. The DataLoader batches one GROUP BY for the whole page.
    public static async Task<int> GetListingCountAsync(
        [Parent] Company company, IListingCountByCompanyIdDataLoader listingCounts, CancellationToken ct) =>
        await listingCounts.LoadAsync(company.Id, ct);

    // Listings you applied to (any status past Saved), to see how often you've tried this company.
    public static async Task<int> GetApplicationCountAsync(
        [Parent] Company company, IApplicationCountByCompanyIdDataLoader applicationCounts, CancellationToken ct) =>
        await applicationCounts.LoadAsync(company.Id, ct);
}

internal static class CompanyDataLoaders
{
    [DataLoader]
    public static async Task<Dictionary<int, int>> GetListingCountByCompanyIdAsync(
        IReadOnlyList<int> companyIds, AppDbContext db, CancellationToken ct) =>
        await db.Listings
            .Where(l => companyIds.Contains(l.CompanyId))
            .GroupBy(l => l.CompanyId)
            .ToDictionaryAsync(g => g.Key, g => g.Count(), ct);

    [DataLoader]
    public static async Task<Dictionary<int, int>> GetApplicationCountByCompanyIdAsync(
        IReadOnlyList<int> companyIds, AppDbContext db, CancellationToken ct) =>
        await db.Listings
            .Where(l => companyIds.Contains(l.CompanyId) && l.Status != ApplicationStatus.Saved)
            .GroupBy(l => l.CompanyId)
            .ToDictionaryAsync(g => g.Key, g => g.Count(), ct);
}
