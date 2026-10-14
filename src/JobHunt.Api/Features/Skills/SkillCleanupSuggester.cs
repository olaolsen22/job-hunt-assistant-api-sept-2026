using System.Text.Json;
using JobHunt.Api.Data;
using JobHunt.Api.Features.AiSafeguards;
using JobHunt.Api.Features.LlmModels;
using JobHunt.Api.Llm;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;

namespace JobHunt.Api.Features.Skills;

public enum SkillCleanupSuggestStatus { Succeeded, Failed }
public record SkillCleanupSuggestion(SkillCleanupSuggestStatus Status, List<SkillCleanupOperation>? Operations, string? Error);

// Only suggests: nothing is saved but usage. The reviewed operations go to cleanUpSkills.
public class SkillCleanupSuggester(LlmExecution execution, LlmModelResolver models, AiOperations operations)
{
    public const string Operation = "suggest-skill-cleanup";

    private record Plan(List<SkillCleanupOperation?>? Operations);

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
        if (call.Error is { } error) result = new(SkillCleanupSuggestStatus.Failed, null, error);
        else if (Decode(call.Response!.Text) is { } plan) result = new(SkillCleanupSuggestStatus.Succeeded, await KnownAsync(plan, db, ct), null);
        else
        {
            call.Usage!.Success = false;
            call.Usage.ErrorCode = "invalid-json";
            result = new(SkillCleanupSuggestStatus.Failed, null, "The AI's answer couldn't be read. Try again.");
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
