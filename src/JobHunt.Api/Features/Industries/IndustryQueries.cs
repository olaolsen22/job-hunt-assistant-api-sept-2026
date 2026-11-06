using HotChocolate.Data.Sorting;
using JobHunt.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace JobHunt.Api.Features.Industries;

[QueryType]
public static class IndustryQueries
{
    // search matches NormalizedName because the built-in `contains` filter is case-sensitive on SQLite.
    [UsePaging]
    [UseProjection]
    [UseFiltering]
    [UseSorting]
    public static IQueryable<Industry> GetIndustries(string? search, AppDbContext db, ISortingContext sorting)
    {
        var industries = sorting.WithDefaultOrder(db.Industries, q => q.OrderBy(i => i.Name));
        if (search is null) return industries;
        var name = Names.Normalize(search);
        return industries.Where(i => i.NormalizedName.Contains(name));
    }
}

[ObjectType<Industry>]
public static partial class IndustryNode
{
    static partial void Configure(IObjectTypeDescriptor<Industry> descriptor)
    {
        descriptor.Ignore(i => i.UserId);
        descriptor.Ignore(i => i.NormalizedName);
    }
}
