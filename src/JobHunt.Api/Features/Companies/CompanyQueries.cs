using JobHunt.Api.Data;
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
    public static IQueryable<Company> GetCompanies(string? search, AppDbContext db)
    {
        var companies = db.Companies.OrderBy(c => c.Name).AsQueryable();
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
        // listingCount needs the Id even when the client didn't select it.
        descriptor.Field(c => c.Id).IsProjected();
    }

    // Computed, never stored, so it can't drift. The DataLoader batches one GROUP BY for the whole page.
    public static async Task<int> GetListingCountAsync(
        [Parent] Company company, IListingCountByCompanyIdDataLoader listingCounts, CancellationToken ct) =>
        await listingCounts.LoadAsync(company.Id, ct);
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
}
