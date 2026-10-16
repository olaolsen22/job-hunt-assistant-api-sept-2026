using HotChocolate.Data.Sorting;
using JobHunt.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace JobHunt.Api.Features.Skills;

[QueryType]
public static class SkillCategoryQueries
{
    // Default order is SortOrder, the order of the grouped lines on a developer CV.
    [UsePaging]
    [UseProjection]
    [UseFiltering]
    [UseSorting]
    public static IQueryable<SkillCategory> GetSkillCategories(AppDbContext db, ISortingContext sorting)
    {
        sorting.Handled(false);
        return sorting.IsDefined ? db.SkillCategories : db.SkillCategories.OrderBy(c => c.SortOrder).ThenBy(c => c.Id);
    }
}

[ObjectType<SkillCategory>]
public static partial class SkillCategoryNode
{
    static partial void Configure(IObjectTypeDescriptor<SkillCategory> descriptor)
    {
        descriptor.Ignore(c => c.UserId);
        descriptor.Ignore(c => c.NormalizedName);
        // skillCount needs the Id even when the client didn't select it.
        descriptor.Field(c => c.Id).IsProjected();
    }

    public static async Task<int> GetSkillCountAsync(
        [Parent] SkillCategory category, ISkillCountByCategoryIdDataLoader skillCounts, CancellationToken ct) =>
        await skillCounts.LoadAsync(category.Id, ct);
}

internal static class SkillCategoryDataLoaders
{
    [DataLoader]
    public static async Task<Dictionary<int, int>> GetSkillCountByCategoryIdAsync(
        IReadOnlyList<int> categoryIds, AppDbContext db, CancellationToken ct) =>
        await db.Skills
            .Where(s => s.CategoryId != null && categoryIds.Contains(s.CategoryId.Value))
            .GroupBy(s => s.CategoryId!.Value)
            .ToDictionaryAsync(g => g.Key, g => g.Count(), ct);
}

[ObjectType<SkillCategoryTranslation>]
public static partial class SkillCategoryTranslationNode
{
    static partial void Configure(IObjectTypeDescriptor<SkillCategoryTranslation> descriptor) =>
        descriptor.Ignore(t => t.SkillCategory);
}
