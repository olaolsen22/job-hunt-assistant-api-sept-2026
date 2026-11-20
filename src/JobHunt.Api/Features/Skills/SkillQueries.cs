using HotChocolate.Data.Sorting;
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
    public static IQueryable<Skill> GetSkills(string? search, AppDbContext db, ISortingContext sorting)
    {
        var skills = sorting.WithDefaultOrder(db.Skills, q => q.OrderBy(s => s.Name));
        if (search is null) return skills;
        var name = Names.Normalize(search);
        return skills.Where(s => s.NormalizedName.Contains(name));
    }

    // Most-requested skills across the user's listings, for a "popular tech" view. since filters on listing CreatedAt.
    public static async Task<List<SkillPopularity>> GetTopSkillsAsync(
        AppDbContext db, CancellationToken ct,
        int limit = 10, DateTime? since = null, bool requiredOnly = false, bool includeArchived = false)
    {
        limit = Math.Clamp(limit, 1, 100);
        var rows = db.ListingSkills.AsQueryable();
        if (!includeArchived) rows = rows.Where(s => s.Listing.ArchivedAt == null);
        if (requiredOnly) rows = rows.Where(s => s.IsRequired);
        if (since is { } from)
        {
            var utc = from.ToUniversalTime();
            rows = rows.Where(s => s.Listing.CreatedAt >= utc);
        }

        var counts = await rows
            .GroupBy(s => s.SkillId)
            .Select(g => new { SkillId = g.Key, Count = g.Count() })
            .OrderByDescending(g => g.Count).ThenBy(g => g.SkillId)
            .Take(limit)
            .ToListAsync(ct);

        var ids = counts.Select(c => c.SkillId).ToList();
        var skills = await db.Skills.Where(s => ids.Contains(s.Id)).ToDictionaryAsync(s => s.Id, ct);
        return [.. counts.Select(c => new SkillPopularity(skills[c.SkillId], c.Count))];
    }
}

public record SkillPopularity(Skill Skill, int ListingCount);

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

    // Null when the skill isn't on the profile, so a listing can mark the skills the user already has.
    public static async Task<int?> GetProfileLevelAsync(
        [Parent] Skill skill, IProfileLevelBySkillIdDataLoader profileLevels, CancellationToken ct) =>
        await profileLevels.LoadAsync(skill.Id, ct);
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

    [DataLoader]
    public static async Task<Dictionary<int, int?>> GetProfileLevelBySkillIdAsync(
        IReadOnlyList<int> skillIds, AppDbContext db, CancellationToken ct) =>
        await db.ProfileSkills
            .Where(s => skillIds.Contains(s.SkillId))
            .ToDictionaryAsync(s => s.SkillId, s => (int?)s.Level, ct);
}

[ObjectType<SkillTranslation>]
public static partial class SkillTranslationNode
{
    static partial void Configure(IObjectTypeDescriptor<SkillTranslation> descriptor) => descriptor.Ignore(t => t.Skill);
}
