using HotChocolate.Data.Sorting;
using JobHunt.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace JobHunt.Api.Features.ListingImports;

[QueryType]
public static class ListingImportQueries
{
    // isReviewed: false lists what's still waiting; omitted lists everything. Newest file first by default.
    [UsePaging]
    [UseProjection]
    [UseFiltering]
    [UseSorting]
    public static IQueryable<ListingImport> GetListingImports(bool? isReviewed, AppDbContext db, ISortingContext sorting)
    {
        var imports = sorting.WithDefaultOrder(db.ListingImports, q => q.OrderByDescending(i => i.GeneratedAt).ThenByDescending(i => i.Id));
        return isReviewed switch
        {
            true => imports.Where(i => i.ReviewedAt != null),
            false => imports.Where(i => i.ReviewedAt == null),
            null => imports,
        };
    }

    // For the "pending imports" notice on app open, without loading the list.
    public static async Task<int> GetPendingListingImportCountAsync(AppDbContext db, CancellationToken ct) =>
        await db.ListingImports.CountAsync(i => i.ReviewedAt == null, ct);
}

[ObjectType<ListingImport>]
public static partial class ListingImportNode
{
    static partial void Configure(IObjectTypeDescriptor<ListingImport> descriptor) =>
        descriptor.Ignore(i => i.UserId);
}
