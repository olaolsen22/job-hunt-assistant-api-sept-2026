using System.Text.Json;
using JobHunt.Api.Data;
using JobHunt.Api.Features.AiSafeguards;
using JobHunt.Api.Features.Listings;
using JobHunt.Api.Features.LlmModels;
using JobHunt.Api.Llm;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;

namespace JobHunt.Api.Features.Skills;

public enum SkillCleanupSuggestStatus { Succeeded, Failed }
public record SkillCleanupSuggestion(SkillCleanupSuggestStatus Status, List<SkillCleanupOperation>? Operations,
    List<SkillCleanupTranslation>? Translations, string? Error);

// Only suggests: nothing is saved but usage. The reviewed operations go to cleanUpSkills.
public class SkillCleanupSuggester(LlmExecution execution, LlmModelResolver models, AiOperations operations)
{
    public const string Operation = "suggest-skill-cleanup";

    private record Plan(List<SkillCleanupOperation?>? Operations, List<Translation?>? Translations);
    // Language as text: an unknown value drops that row instead of failing the whole answer.
    private record Translation(string? SkillName, string? Language, string? Name);

    public Task<SkillCleanupSuggestion> SuggestAsync(string? model, AppDbContext db, CancellationToken ct, Guid? requestId = null) =>
        operations.RunAsync(Operation, new { model }, requestId, db,
            () => models.ResolveAsync(model, db, ct), resolved => SuggestCoreAsync(resolved, db, ct),
            result => new AiOutcome(result.Status == SkillCleanupSuggestStatus.Succeeded), ct);

    private async Task<SkillCleanupSuggestion> SuggestCoreAsync(ResolvedLlmModel resolved, AppDbContext db, CancellationToken ct)
    {
        var call = await execution.RunAsync(resolved,
            [new(ChatRole.System, SkillCleanupPromptQueries.Instructions), new(ChatRole.User, await SkillCleanupPromptQueries.SkillListAsync(db, ct))],
            new ChatOptions { ResponseFormat = ChatResponseFormat.Json, Temperature = 0, MaxOutputTokens = 16_384 },
            Operation, db, ct);
        SkillCleanupSuggestion result;
        if (call.Error is { } error) result = new(SkillCleanupSuggestStatus.Failed, null, null, error);
        else if (Decode(call.Response!.Text) is { } plan)
        {
            var known = await KnownAsync(plan, db, ct);
            result = new(SkillCleanupSuggestStatus.Succeeded, known, await TranslationsAsync(plan, known, db, ct), null);
        }
        else
        {
            call.Usage!.Success = false;
            call.Usage.ErrorCode = "invalid-json";
            result = new(SkillCleanupSuggestStatus.Failed, null, null, "The AI's answer couldn't be read. Try again.");
        }
        if (call.Usage is { } audited) await execution.SaveUsageAsync(audited, ct);
        return result;
    }

    private static Plan? Decode(string text)
    {
        try { return JsonSerializer.Deserialize<Plan>(text, JsonSerializerOptions.Web); }
        catch (JsonException) { return null; }
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
