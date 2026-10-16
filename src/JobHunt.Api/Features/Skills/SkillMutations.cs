using JobHunt.Api.Data;
using JobHunt.Api.Features.Listings;
using JobHunt.Api.Features.Profiles;
using JobHunt.Api.Features.TailoredCvDrafts;
using Microsoft.EntityFrameworkCore;

namespace JobHunt.Api.Features.Skills;

public class SkillNotFoundException(int id) : Exception($"Skill {id} was not found.");

public class InvalidSkillCleanupException(string message) : Exception(message);

public class SkillInUseException(int id) : Exception($"Skill {id} is still on the profile or a listing; merge it instead.");

// Replaces skillIds with names: one name merges or renames ("Vue", "Vue.js" → "Vue"), several split ("C#/.NET" → "C#", ".NET").
public record SkillCleanupOperation(List<int> SkillIds, List<string> Names);

// By skill name, so a cleanup can translate a skill it creates; applied after the operations.
public record SkillCleanupTranslation(string SkillName, Language Language, string Name);

[MutationType]
public static class SkillMutations
{
    private const int MaxOperations = 200;
    internal const int MaxNamesPerOperation = 10;
    private const int MaxNameLength = 200;

    // Null clears it. Set on the lookup, so listings, the profile and new CV drafts all share it.
    [Error<SkillNotFoundException>]
    [Error<SkillCategoryNotFoundException>]
    public static async Task<Skill> SetSkillCategoryAsync(int skillId, int? categoryId, AppDbContext db, CancellationToken ct)
    {
        var skill = await db.Skills.FirstOrDefaultAsync(s => s.Id == skillId, ct) ?? throw new SkillNotFoundException(skillId);
        skill.Category = categoryId is { } id
            ? await db.SkillCategories.FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw new SkillCategoryNotFoundException(id)
            : null;
        skill.CategoryId = categoryId;
        await db.SaveChangesAsync(ct);
        return skill;
    }

    // Only for unused skills (typos, unneeded starter skills): one in use is merged instead, so nothing loses a skill.
    [Error<SkillNotFoundException>]
    [Error<SkillInUseException>]
    [UseMutationConvention(PayloadFieldName = "id")]
    public static async Task<int> DeleteSkillAsync(int id, AppDbContext db, CancellationToken ct)
    {
        var skill = await db.Skills.FirstOrDefaultAsync(s => s.Id == id, ct) ?? throw new SkillNotFoundException(id);
        if (await db.ProfileSkills.AnyAsync(s => s.SkillId == id, ct)
            || await db.ListingSkills.AnyAsync(s => s.SkillId == id, ct)
            || await db.ProfileExperiences.AnyAsync(e => e.Skills.Any(s => s.Id == id), ct)
            || await db.ProfileProjects.AnyAsync(p => p.Skills.Any(s => s.Id == id), ct))
            throw new SkillInUseException(id);
        db.Skills.Remove(skill);
        await db.SaveChangesAsync(ct);
        return id;
    }

    // Upsert. Only for skills that are words; a product name needs none and prints as is.
    [Error<SkillNotFoundException>]
    [Error<InvalidSkillCleanupException>]
    public static async Task<Skill> SetSkillTranslationAsync(
        int skillId, Language language, string name, AppDbContext db, CancellationToken ct)
    {
        var text = name.Trim();
        if (text.Length is 0 or > SkillTranslation.MaxNameLength)
            throw new InvalidSkillCleanupException($"A translation needs 1–{SkillTranslation.MaxNameLength} characters.");
        var skill = await db.Skills.Include(s => s.Translations).FirstOrDefaultAsync(s => s.Id == skillId, ct)
            ?? throw new SkillNotFoundException(skillId);
        SetTranslation(skill, language, text);
        await db.SaveChangesAsync(ct);
        return skill;
    }

    // Removing a missing translation is a no-op, so the UI can always call it.
    [Error<SkillNotFoundException>]
    public static async Task<Skill> DeleteSkillTranslationAsync(int skillId, Language language, AppDbContext db, CancellationToken ct)
    {
        var skill = await db.Skills.Include(s => s.Translations).FirstOrDefaultAsync(s => s.Id == skillId, ct)
            ?? throw new SkillNotFoundException(skillId);
        skill.Translations.RemoveAll(t => t.Language == language);
        await db.SaveChangesAsync(ct);
        return skill;
    }

    private static void SetTranslation(Skill skill, Language language, string name)
    {
        if (skill.Translations.FirstOrDefault(t => t.Language == language) is { } existing) existing.Name = name;
        else skill.Translations.Add(new SkillTranslation { Language = language, Name = name });
    }

    // Works on the shared lookup, so the profile, experience/project tags and listings are all cleaned at once.
    // One transaction, so a reviewed plan is applied fully or not at all.
    [Error<SkillNotFoundException>]
    [Error<InvalidSkillCleanupException>]
    [UseMutationConvention(PayloadFieldName = "skills")]
    public static async Task<List<Skill>> CleanUpSkillsAsync(
        List<SkillCleanupOperation> operations, AppDbContext db, CancellationToken ct,
        List<SkillCleanupTranslation>? translations = null)
    {
        translations ??= [];
        Validate(operations, translations);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var result = new List<Skill>();
        foreach (var operation in operations) result.AddRange(await ApplyAsync(operation, db, ct));
        foreach (var translation in translations)
        {
            var normalized = Names.Normalize(translation.SkillName);
            var skill = await db.Skills.Include(s => s.Translations).FirstOrDefaultAsync(s => s.NormalizedName == normalized, ct)
                ?? throw new InvalidSkillCleanupException($"There is no skill \"{translation.SkillName.Trim()}\" to translate.");
            SetTranslation(skill, translation.Language, translation.Name.Trim());
            await db.SaveChangesAsync(ct);
            result.Add(skill);
        }
        await transaction.CommitAsync(ct);
        return [.. result.Distinct().OrderBy(s => s.Name)];
    }

    private static void Validate(List<SkillCleanupOperation> operations, List<SkillCleanupTranslation> translations)
    {
        if (operations.Count + translations.Count == 0 || operations.Count > MaxOperations || translations.Count > MaxOperations)
            throw new InvalidSkillCleanupException($"Send 1–{MaxOperations} operations or translations.");
        if (translations.Any(t => string.IsNullOrWhiteSpace(t.SkillName) || string.IsNullOrWhiteSpace(t.Name)
                || t.Name.Trim().Length > SkillTranslation.MaxNameLength))
            throw new InvalidSkillCleanupException($"Translations need a skill and a name of at most {SkillTranslation.MaxNameLength} characters.");
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
        var category = sources.Select(s => s.CategoryId).FirstOrDefault(c => c is not null);
        var targets = names.Select(name =>
        {
            var skill = existing.GetValueOrDefault(Names.Normalize(name));
            if (skill is null)
                db.Skills.Add(skill = new Skill { UserId = sources[0].UserId, NormalizedName = Names.Normalize(name) });
            skill.Name = name;
            skill.CategoryId ??= category;
            return skill;
        }).ToList();
        await db.SaveChangesAsync(ct);

        var targetIds = targets.Select(t => t.Id).ToList();
        var removed = sources.Where(s => !targetIds.Contains(s.Id)).ToList();
        var removedIds = removed.Select(s => s.Id).ToList();
        if (removedIds.Count == 0) return targets;

        // A target's own row (the profile's or a listing's) keeps its claim as it is; without one, the target
        // takes the strongest merged row, so a merge never drops a level the user or the listing set.
        var profileSkills = await db.ProfileSkills
            .Where(s => removedIds.Contains(s.SkillId) || targetIds.Contains(s.SkillId)).ToListAsync(ct);
        foreach (var group in profileSkills.GroupBy(s => s.ProfileId).Where(g => g.Any(s => removedIds.Contains(s.SkillId))))
        {
            var level = group.Where(s => removedIds.Contains(s.SkillId)).Max(s => s.Level);
            foreach (var targetId in targetIds.Where(id => group.All(s => s.SkillId != id)))
                db.ProfileSkills.Add(new ProfileSkill { ProfileId = group.Key, SkillId = targetId, Level = level });
            db.ProfileSkills.RemoveRange(group.Where(s => removedIds.Contains(s.SkillId)));
        }

        var listingSkills = await db.ListingSkills
            .Where(s => removedIds.Contains(s.SkillId) || targetIds.Contains(s.SkillId)).ToListAsync(ct);
        foreach (var group in listingSkills.GroupBy(s => s.ListingId).Where(g => g.Any(s => removedIds.Contains(s.SkillId))))
        {
            var merged = group.Where(s => removedIds.Contains(s.SkillId)).ToList();
            var strongest = merged.OrderByDescending(s => s.Level).ThenByDescending(s => s.IsRequired).First();
            foreach (var targetId in targetIds.Where(id => group.All(s => s.SkillId != id)))
                db.ListingSkills.Add(new ListingSkill
                {
                    ListingId = group.Key, SkillId = targetId,
                    Level = strongest.Level, MinYears = strongest.MinYears, IsRequired = strongest.IsRequired,
                });
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

        // A merge keeps the sources' translations where the target has none; a split can't divide one translation.
        if (targets is [var target])
        {
            var translations = await db.SkillTranslations
                .Where(t => removedIds.Contains(t.SkillId) || t.SkillId == target.Id).ToListAsync(ct);
            foreach (var group in translations.Where(t => t.SkillId != target.Id).GroupBy(t => t.Language))
                if (!translations.Any(t => t.SkillId == target.Id && t.Language == group.Key)) group.First().SkillId = target.Id;
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
