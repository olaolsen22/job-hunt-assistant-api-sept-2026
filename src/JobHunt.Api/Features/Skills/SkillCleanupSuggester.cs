using JobHunt.Api.Data;
using JobHunt.Api.Features.AiSafeguards;
using JobHunt.Api.Features.Listings;
using JobHunt.Api.Features.LlmModels;
using JobHunt.Api.Features.Parsing;
using JobHunt.Api.Llm;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;

namespace JobHunt.Api.Features.Skills;

public enum SkillCleanupSuggestStatus { Succeeded, Failed }
public record SkillCleanupSuggestion(SkillCleanupSuggestStatus Status, List<SkillCleanupOperation>? Operations,
    List<SkillCleanupTranslation>? Translations, List<SkillCleanupCategory>? Categories,
    List<SkillCleanupRemoval>? Removals, string? Error, [property: GraphQLIgnore] string? ErrorCode = null);

// Only suggests: nothing is saved but usage. The reviewed operations go to cleanUpSkills.
public class SkillCleanupSuggester(LlmExecution execution, LlmModelResolver models, AiOperations operations)
{
    public const string Operation = "suggest-skill-cleanup";

    private record Plan(List<SkillCleanupOperation?>? Operations, List<Translation?>? Translations, List<Category?>? Categories,
        List<Removal?>? Removals);
    private record Removal(int? SkillId, string? Reason);
    private record Category(string? SkillName, string? CategoryName);
    // Language as text: an unknown value drops that row instead of failing the whole answer.
    private record Translation(string? SkillName, string? Language, string? Name);

    public Task<SkillCleanupSuggestion> SuggestAsync(string? model, AppDbContext db, CancellationToken ct, Guid? requestId = null) =>
        operations.RunAsync(Operation, new { model }, requestId, db,
            () => models.ResolveAsync(model, db, ct), resolved => SuggestCoreAsync(resolved, db, ct),
            result => new AiOutcome(result.Status == SkillCleanupSuggestStatus.Succeeded, result.ErrorCode), ct);

    private async Task<SkillCleanupSuggestion> SuggestCoreAsync(ResolvedLlmModel resolved, AppDbContext db, CancellationToken ct)
    {
        var call = await execution.RunJsonAsync(resolved,
            [new(ChatRole.System, SkillCleanupPromptQueries.Instructions), new(ChatRole.User, await SkillCleanupPromptQueries.SkillListAsync(db, ct))],
            new ChatOptions { ResponseFormat = ChatResponseFormat.Json, Temperature = 0, MaxOutputTokens = 16_384 },
            Operation, db, JsonReply.TryDecode<Plan>, "The AI's answer couldn't be read. Try again.", ct);
        if (call.Value is not { } plan) return new(SkillCleanupSuggestStatus.Failed, null, null, null, null, call.Error, call.ErrorCode);
        var known = await KnownAsync(plan, db, ct);
        return new(SkillCleanupSuggestStatus.Succeeded, known, await TranslationsAsync(plan, known, db, ct),
            await CategoriesAsync(plan, known, db, ct), await RemovalsAsync(plan, known, db, ct), null);
    }

    // Drops invented ids, blank names and no-op renames, so the review only shows changes cleanUpSkills can apply.
    private static async Task<List<SkillCleanupOperation>> KnownAsync(Plan plan, AppDbContext db, CancellationToken ct)
    {
        var skills = await db.Skills.ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var used = new HashSet<int>();
        var result = new List<SkillCleanupOperation>();
        foreach (var operation in plan.Operations ?? [])
        {
            var ids = operation?.SkillIds?.Where(skills.ContainsKey).Distinct().Where(used.Add).ToList() ?? [];
            var names = operation?.Names?.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim())
                .DistinctBy(Names.Normalize).Take(SkillMutations.MaxNamesPerOperation).ToList() ?? [];
            if (ids.Count == 0 || names.Count == 0) continue;
            if (ids.Count == 1 && names.Count == 1 && skills[ids[0]] == names[0]) continue;
            result.Add(new(ids, names));
        }
        return result;
    }

    // Keeps translations of a skill that exists or that an operation creates, unless the skill already has that name.
    private static async Task<List<SkillCleanupTranslation>> TranslationsAsync(
        Plan plan, List<SkillCleanupOperation> operations, AppDbContext db, CancellationToken ct)
    {
        var skills = await db.Skills.Include(s => s.Translations).ToDictionaryAsync(s => s.NormalizedName, ct);
        var targets = operations.SelectMany(o => o.Names).Select(Names.Normalize).ToHashSet();
        return [.. (plan.Translations ?? []).OfType<Translation>()
            .Select(t => (SkillName: t.SkillName?.Trim(), Name: t.Name?.Trim(),
                Language: Enum.TryParse<Language>(t.Language, true, out var language) ? language : (Language?)null))
            .Where(t => !string.IsNullOrEmpty(t.SkillName) && t.Name is { Length: > 0 and <= SkillTranslation.MaxNameLength }
                && t.Language is not null
                && (targets.Contains(Names.Normalize(t.SkillName)) || skills.ContainsKey(Names.Normalize(t.SkillName)))
                && !(skills.GetValueOrDefault(Names.Normalize(t.SkillName)) is { } skill
                    && (skill.Name == t.Name || skill.Translations.Any(x => x.Language == t.Language && x.Name == t.Name))))
            .DistinctBy(t => (Names.Normalize(t.SkillName!), t.Language))
            .Select(t => new SkillCleanupTranslation(t.SkillName!, t.Language!.Value, t.Name!))];
    }

    // Only skills that exist and no operation touches, so cleanUpSkills accepts the reviewed set as is.
    private static async Task<List<SkillCleanupRemoval>> RemovalsAsync(
        Plan plan, List<SkillCleanupOperation> operations, AppDbContext db, CancellationToken ct)
    {
        var ids = await db.Skills.Select(s => s.Id).ToHashSetAsync(ct);
        var changed = operations.SelectMany(o => o.SkillIds).ToHashSet();
        return [.. (plan.Removals ?? []).OfType<Removal>()
            .Where(r => r.SkillId is { } id && ids.Contains(id) && !changed.Contains(id))
            .DistinctBy(r => r.SkillId)
            .Select(r => new SkillCleanupRemoval(r.SkillId!.Value, r.Reason?.Trim() is { Length: > 0 } reason ? reason : "Not a skill"))];
    }

    // A suggestion only fills an uncategorized skill, existing or created without inheriting. Unknown names are dropped.
    private static async Task<List<SkillCleanupCategory>> CategoriesAsync(
        Plan plan, List<SkillCleanupOperation> operations, AppDbContext db, CancellationToken ct)
    {
        var skills = await db.Skills.ToDictionaryAsync(s => s.NormalizedName, ct);
        var categories = await db.SkillCategories.ToDictionaryAsync(c => c.NormalizedName, c => c.Name, ct);
        var inherited = operations.Where(o => o.SkillIds.Any(id => skills.Values.Any(s => s.Id == id && s.CategoryId is not null)))
            .SelectMany(o => o.Names).Select(Names.Normalize).ToHashSet();
        var targets = operations.SelectMany(o => o.Names).Select(Names.Normalize).Where(n => !inherited.Contains(n)).ToHashSet();
        return [.. (plan.Categories ?? []).OfType<Category>()
            .Select(c => (SkillName: c.SkillName?.Trim(), CategoryName: c.CategoryName?.Trim()))
            .Where(c => !string.IsNullOrEmpty(c.SkillName) && !string.IsNullOrEmpty(c.CategoryName)
                && categories.ContainsKey(Names.Normalize(c.CategoryName))
                && (skills.GetValueOrDefault(Names.Normalize(c.SkillName)) is { } skill
                    ? skill.CategoryId is null && !inherited.Contains(Names.Normalize(c.SkillName))
                    : targets.Contains(Names.Normalize(c.SkillName))))
            .DistinctBy(c => Names.Normalize(c.SkillName!))
            .Select(c => new SkillCleanupCategory(c.SkillName!, categories[Names.Normalize(c.CategoryName!)]))];
    }
}

[MutationType]
public static class SkillCleanupSuggestMutations
{
    [Error<UnknownLlmModelException>]
    [Error<AiSafetyException>]
    [UseMutationConvention(PayloadFieldName = "result")]
    public static Task<SkillCleanupSuggestion> SuggestSkillCleanupAsync(
        string? model, AppDbContext db, SkillCleanupSuggester suggester, CancellationToken ct, Guid? requestId = null) =>
        suggester.SuggestAsync(model, db, ct, requestId);
}
