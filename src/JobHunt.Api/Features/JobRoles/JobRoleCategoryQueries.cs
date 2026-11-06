using HotChocolate.Data.Sorting;
using JobHunt.Api.Data;

namespace JobHunt.Api.Features.JobRoles;

[QueryType]
public static class JobRoleCategoryQueries
{
    [UsePaging]
    [UseProjection]
    [UseFiltering]
    [UseSorting]
    public static IQueryable<JobRoleCategory> GetJobRoleCategories(string? search, AppDbContext db, ISortingContext sorting)
    {
        var query = sorting.WithDefaultOrder(db.JobRoleCategories, q => q.OrderBy(c => c.Name));
        if (string.IsNullOrWhiteSpace(search)) return query;
        var name = Names.Normalize(search);
        return query.Where(c => c.NormalizedName.Contains(name));
    }
}

[ObjectType<JobRoleCategory>]
public static partial class JobRoleCategoryNode
{
    static partial void Configure(IObjectTypeDescriptor<JobRoleCategory> descriptor)
    {
        descriptor.Ignore(c => c.UserId);
        descriptor.Ignore(c => c.NormalizedName);
    }
}
