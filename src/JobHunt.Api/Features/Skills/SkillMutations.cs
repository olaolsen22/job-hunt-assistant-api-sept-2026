using JobHunt.Api.Data;
using JobHunt.Api.Features.Listings;
using JobHunt.Api.Features.Profiles;
using JobHunt.Api.Features.TailoredCvDrafts;
using Microsoft.EntityFrameworkCore;

namespace JobHunt.Api.Features.Skills;

public class SkillNotFoundException(int id) : Exception($"Skill {id} was not found.");

public class InvalidSkillCleanupException(string message) : Exception(message);

// Replaces skillIds with names: one name merges or renames ("Vue", "Vue.js" → "Vue"), several split ("C#/.NET" → "C#", ".NET").
public record SkillCleanupOperation(List<int> SkillIds, List<string> Names);

[MutationType]
public static class SkillMutations
{
    private const int MaxOperations = 200;
    internal const int MaxNamesPerOperation = 10;
    private const int MaxNameLength = 200;

    // Null clears it. Set on the lookup, so listings, the profile and new CV drafts all share it.
    [Error<SkillNotFoundException>]
    public static async Task<Skill> SetSkillCategoryAsync(int skillId, SkillCategory? category, AppDbContext db, CancellationToken ct)
    {
        var skill = await db.Skills.FirstOrDefaultAsync(s => s.Id == skillId, ct) ?? throw new SkillNotFoundException(skillId);
        skill.Category = category;
        await db.SaveChangesAsync(ct);
        return skill;
    }

    // Works on the shared lookup, so the profile, experience/project tags and listings are all cleaned at once.
    // One transaction, so a reviewed plan is applied fully or not at all.
    [Error<SkillNotFoundException>]
    [Error<InvalidSkillCleanupException>]
    [UseMutationConvention(PayloadFieldName = "skills")]
    public static async Task<List<Skill>> CleanUpSkillsAsync(
        List<SkillCleanupOperation> operations, AppDbContext db, CancellationToken ct)
    {
        Validate(operations);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var result = new List<Skill>();
        foreach (var operation in operations) result.AddRange(await ApplyAsync(operation, db, ct));
        await transaction.CommitAsync(ct);
        return [.. result.Distinct().OrderBy(s => s.Name)];
    }

    private static void Validate(List<SkillCleanupOperation> operations)
    {
        if (operations.Count is 0 or > MaxOperations)
            throw new InvalidSkillCleanupException($"Send 1–{MaxOperations} operations.");
        foreach (var operation in operations)
        {
            if (operation.SkillIds.Count == 0) throw new InvalidSkillCleanupException("Each operation needs at least one skill.");
            if (operation.Names.Count is 0 or > MaxNamesPerOperation)
                throw new InvalidSkillCleanupException($"Each operation needs 1–{MaxNamesPerOperation} names.");
            if (operation.Names.Any(n => string.IsNullOrWhiteSpace(n) || n.Trim().Length > MaxNameLength))
                throw new InvalidSkillCleanupException($"Skill names must be nonblank and at most {MaxNameLength} characters.");
        }
        // A skill in two operations would be merged by the first and missing for the second.
        var repeated = operations.SelectMany(o => o.SkillIds.Distinct()).GroupBy(id => id).FirstOrDefault(g => g.Count() > 1);
        if (repeated is not null) throw new InvalidSkillCleanupException($"Skill {repeated.Key} is in more than one operation.");
    }

    private static async Task<List<Skill>> ApplyAsync(SkillCleanupOperation operation, AppDbContext db, CancellationToken ct)
    {
        var sourceIds = operation.SkillIds.Distinct().ToList();
        var sources = await db.Skills.Where(s => sourceIds.Contains(s.Id)).ToListAsync(ct);
        if (sources.Count != sourceIds.Count)
            throw new SkillNotFoundException(sourceIds.Except(sources.Select(s => s.Id)).First());

        // A name matching an existing skill (a source or another one) reuses it, so the plan also sets its spelling.
        var names = operation.Names.Select(n => n.Trim()).DistinctBy(Names.Normalize).ToList();
        var normalized = names.Select(Names.Normalize).ToList();
        var existing = await db.Skills.Where(s => normalized.Contains(s.NormalizedName)).ToDictionaryAsync(s => s.NormalizedName, ct);
        var category = sources.Select(s => s.Category).FirstOrDefault(c => c is not null);
        var targets = names.Select(name =>
        {
            var skill = existing.GetValueOrDefault(Names.Normalize(name));
            if (skill is null)
                db.Skills.Add(skill = new Skill { UserId = sources[0].UserId, NormalizedName = Names.Normalize(name) });
            skill.Name = name;
            skill.Category ??= category;
            return skill;
        }).ToList();
        await db.SaveChangesAsync(ct);

        var targetIds = targets.Select(t => t.Id).ToList();
        var removed = sources.Where(s => !targetIds.Contains(s.Id)).ToList();
        var removedIds = removed.Select(s => s.Id).ToList();
        if (removedIds.Count == 0) return targets;

        // Merged rows keep the strongest claim: highest level, required if any was.
        var profileSkills = await db.ProfileSkills
            .Where(s => removedIds.Contains(s.SkillId) || targetIds.Contains(s.SkillId)).ToListAsync(ct);
        foreach (var group in profileSkills.GroupBy(s => s.ProfileId).Where(g => g.Any(s => removedIds.Contains(s.SkillId))))
        {
            var level = group.Where(s => removedIds.Contains(s.SkillId)).Max(s => s.Level);
            foreach (var targetId in targetIds)
            {
                if (group.FirstOrDefault(s => s.SkillId == targetId) is { } row) row.Level = Math.Max(row.Level, level);
                else db.ProfileSkills.Add(new ProfileSkill { ProfileId = group.Key, SkillId = targetId, Level = level });
            }
            db.ProfileSkills.RemoveRange(group.Where(s => removedIds.Contains(s.SkillId)));
        }

        var listingSkills = await db.ListingSkills
            .Where(s => removedIds.Contains(s.SkillId) || targetIds.Contains(s.SkillId)).ToListAsync(ct);
        foreach (var group in listingSkills.GroupBy(s => s.ListingId).Where(g => g.Any(s => removedIds.Contains(s.SkillId))))
        {
            var merged = group.Where(s => removedIds.Contains(s.SkillId)).ToList();
            foreach (var targetId in targetIds)
            {
                var row = group.FirstOrDefault(s => s.SkillId == targetId);
                if (row is null) db.ListingSkills.Add(row = new ListingSkill { ListingId = group.Key, SkillId = targetId });
                var all = merged.Append(row).ToList();
                row.Level = all.Max(s => s.Level);
                row.MinYears = all.Max(s => s.MinYears);
                row.IsRequired = all.Any(s => s.IsRequired);
            }
            db.ListingSkills.RemoveRange(merged);
        }

        var experiences = await db.ProfileExperiences.Include(e => e.Skills)
            .Where(e => e.Skills.Any(s => removedIds.Contains(s.Id))).ToListAsync(ct);
        foreach (var experience in experiences) ReplaceTags(experience.Skills, removedIds, targets);
        var projects = await db.ProfileProjects.Include(p => p.Skills)
            .Where(p => p.Skills.Any(s => removedIds.Contains(s.Id))).ToListAsync(ct);
        foreach (var project in projects) ReplaceTags(project.Skills, removedIds, targets);

        // Drafts are snapshots, so only the source link moves; a dangling id would block saving the draft.
        var drafts = await db.TailoredCvDrafts.ToListAsync(ct);
        foreach (var draft in drafts)
        {
            var content = draft.Content;
            if (content.Skills?.Any(s => s.SourceSkillId is { } id && removedIds.Contains(id)) != true) continue;
            draft.Content = content with
            {
                Skills = [.. content.Skills.Select(s => s.SourceSkillId is { } id && removedIds.Contains(id)
                    ? s with { SourceSkillId = targetIds[0] } : s)],
            };
        }

        db.Skills.RemoveRange(removed);
        await db.SaveChangesAsync(ct);
        return targets;
    }

    private static void ReplaceTags(List<Skill> tags, List<int> removedIds, List<Skill> targets)
    {
        tags.RemoveAll(s => removedIds.Contains(s.Id));
        tags.AddRange(targets.Where(t => !tags.Contains(t)));
    }
}
