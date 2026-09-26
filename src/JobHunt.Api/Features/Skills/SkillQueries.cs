using JobHunt.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace JobHunt.Api.Features.Skills;

[QueryType]
public static class SkillQueries
{
    // search matches NormalizedName because the built-in `contains` filter is case-sensitive on SQLite.
    [UsePaging]
    [UseProjection]
    [UseFiltering]
    [UseSorting]
    public static IQueryable<Skill> GetSkills(string? search, AppDbContext db)
    {
        var skills = db.Skills.OrderBy(s => s.Name).AsQueryable();
        if (search is null) return skills;
        var name = Names.Normalize(search);
        return skills.Where(s => s.NormalizedName.Contains(name));
    }
}

[ObjectType<Skill>]
public static partial class SkillNode
{
    static partial void Configure(IObjectTypeDescriptor<Skill> descriptor)
    {
        descriptor.Ignore(s => s.UserId);
        descriptor.Ignore(s => s.NormalizedName);
        // listingCount needs the Id even when the client didn't select it.
        descriptor.Field(s => s.Id).IsProjected();
    }

    public static async Task<int> GetListingCountAsync(
        [Parent] Skill skill, IListingCountBySkillIdDataLoader listingCounts, CancellationToken ct) =>
        await listingCounts.LoadAsync(skill.Id, ct);
}

internal static class SkillDataLoaders
{
    [DataLoader]
    public static async Task<Dictionary<int, int>> GetListingCountBySkillIdAsync(
        IReadOnlyList<int> skillIds, AppDbContext db, CancellationToken ct) =>
        await db.ListingSkills
            .Where(s => skillIds.Contains(s.SkillId))
            .GroupBy(s => s.SkillId)
            .ToDictionaryAsync(g => g.Key, g => g.Count(), ct);
}
