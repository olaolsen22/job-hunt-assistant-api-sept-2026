using JobHunt.Api.Data;
using JobHunt.Api.Features.Listings;
using Microsoft.EntityFrameworkCore;

namespace JobHunt.Api.Features.Skills;

public class InvalidSkillCategoryException(string message) : Exception(message);
public class SkillCategoryNotFoundException(int id) : Exception($"Skill category {id} was not found.");
public class DuplicateSkillCategoryException(int existingCategoryId) : Exception("A category with this name already exists.")
{
    public int ExistingCategoryId { get; } = existingCategoryId;
}

[MutationType]
public static class SkillCategoryMutations
{
    // Added last, so it prints after the existing lines until moved. Reuses a category with the same name.
    [Error<InvalidSkillCategoryException>]
    public static async Task<SkillCategory> CreateSkillCategoryAsync(
        string name, AppDbContext db, CancellationToken ct, string? description = null)
    {
        name = ValidateName(name);
        var normalized = Names.Normalize(name);
        var existing = await db.SkillCategories.FirstOrDefaultAsync(c => c.NormalizedName == normalized, ct);
        if (existing is not null) return existing;
        var sortOrder = await db.SkillCategories.MaxAsync(c => (int?)c.SortOrder, ct) + 1 ?? 0;
        var category = new SkillCategory
        {
            UserId = db.CurrentUserId!, Name = name, NormalizedName = normalized,
            Description = ValidateDescription(description), SortOrder = sortOrder,
        };
        db.SkillCategories.Add(category);
        await db.SaveChangesAsync(ct);
        return category;
    }

    // Full replace: a null description clears it.
    [Error<InvalidSkillCategoryException>]
    [Error<SkillCategoryNotFoundException>]
    [Error<DuplicateSkillCategoryException>]
    public static async Task<SkillCategory> UpdateSkillCategoryAsync(
        int id, string name, AppDbContext db, CancellationToken ct, string? description = null)
    {
        var category = await db.SkillCategories.FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new SkillCategoryNotFoundException(id);
        name = ValidateName(name);
        var normalized = Names.Normalize(name);
        var existingId = await db.SkillCategories.Where(c => c.Id != id && c.NormalizedName == normalized)
            .Select(c => (int?)c.Id).FirstOrDefaultAsync(ct);
        if (existingId is { } duplicate) throw new DuplicateSkillCategoryException(duplicate);
        category.Name = name;
        category.NormalizedName = normalized;
        category.Description = ValidateDescription(description);
        await db.SaveChangesAsync(ct);
        return category;
    }

    [Error<SkillCategoryNotFoundException>]
    [Error<InvalidSkillCategoryException>]
    [UseMutationConvention(PayloadFieldName = "skillCategories")]
    public static async Task<List<SkillCategory>> MoveSkillCategoryAsync(
        int id, int position, AppDbContext db, CancellationToken ct)
    {
        var categories = await db.SkillCategories.OrderBy(c => c.SortOrder).ThenBy(c => c.Id).ToListAsync(ct);
        var category = categories.Find(c => c.Id == id) ?? throw new SkillCategoryNotFoundException(id);
        if (position < 0 || position >= categories.Count)
            throw new InvalidSkillCategoryException($"Position must be 0–{categories.Count - 1}.");
        categories.Remove(category);
        categories.Insert(position, category);
        for (var i = 0; i < categories.Count; i++) categories[i].SortOrder = i;
        await db.SaveChangesAsync(ct);
        return categories;
    }

    // Its skills become uncategorized. Drafts lose the id too, so a saved draft doesn't point at a missing category.
    [Error<SkillCategoryNotFoundException>]
    [UseMutationConvention(PayloadFieldName = "id")]
    public static async Task<int> DeleteSkillCategoryAsync(int id, AppDbContext db, CancellationToken ct)
    {
        var category = await db.SkillCategories.FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new SkillCategoryNotFoundException(id);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        foreach (var draft in await db.TailoredCvDrafts.ToListAsync(ct))
        {
            var content = draft.Content;
            if (content.Skills?.Any(s => s.CategoryId == id) != true) continue;
            draft.Content = content with
            {
                Skills = [.. content.Skills.Select(s => s.CategoryId == id ? s with { CategoryId = null } : s)],
            };
        }
        db.SkillCategories.Remove(category);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return id;
    }

    // Upsert: the PDF heading for CVs in that language.
    [Error<SkillCategoryNotFoundException>]
    [Error<InvalidSkillCategoryException>]
    public static async Task<SkillCategory> SetSkillCategoryTranslationAsync(
        int categoryId, Language language, string name, AppDbContext db, CancellationToken ct)
    {
        var text = ValidateName(name);
        var category = await db.SkillCategories.Include(c => c.Translations).FirstOrDefaultAsync(c => c.Id == categoryId, ct)
            ?? throw new SkillCategoryNotFoundException(categoryId);
        if (category.Translations.FirstOrDefault(t => t.Language == language) is { } existing) existing.Name = text;
        else category.Translations.Add(new SkillCategoryTranslation { Language = language, Name = text });
        await db.SaveChangesAsync(ct);
        return category;
    }

    // Removing a missing translation is a no-op, so the UI can always call it.
    [Error<SkillCategoryNotFoundException>]
    public static async Task<SkillCategory> DeleteSkillCategoryTranslationAsync(
        int categoryId, Language language, AppDbContext db, CancellationToken ct)
    {
        var category = await db.SkillCategories.Include(c => c.Translations).FirstOrDefaultAsync(c => c.Id == categoryId, ct)
            ?? throw new SkillCategoryNotFoundException(categoryId);
        category.Translations.RemoveAll(t => t.Language == language);
        await db.SaveChangesAsync(ct);
        return category;
    }

    private static string ValidateName(string name)
    {
        name = name.Trim();
        if (name.Length is 0 or > SkillCategory.MaxNameLength)
            throw new InvalidSkillCategoryException($"Category names must contain 1 to {SkillCategory.MaxNameLength} characters.");
        return name;
    }

    private static string? ValidateDescription(string? description)
    {
        description = description?.Trim();
        if (description?.Length > SkillCategory.MaxDescriptionLength)
            throw new InvalidSkillCategoryException($"Descriptions must be at most {SkillCategory.MaxDescriptionLength} characters.");
        return string.IsNullOrEmpty(description) ? null : description;
    }
}
