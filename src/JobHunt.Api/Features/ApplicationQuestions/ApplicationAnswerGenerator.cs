using System.Text.Json;
using JobHunt.Api.Data;
using JobHunt.Api.Features.AiSafeguards;
using JobHunt.Api.Features.CoverLetters;
using JobHunt.Api.Features.Listings;
using JobHunt.Api.Features.LlmModels;
using JobHunt.Api.Llm;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;

namespace JobHunt.Api.Features.ApplicationQuestions;

public enum ApplicationAnswerGenerationStatus { Succeeded, Failed }

// Candidates: one per question on the form, ordered by question id.
public record ApplicationAnswerGenerationResult(ApplicationAnswerGenerationStatus Status,
    IReadOnlyList<ApplicationAnswerCandidate>? Candidates, string? Error, string? ErrorCode);

// One call answers the whole form, so each answer knows which topics the other questions cover.
public class ApplicationAnswerGenerator(LlmExecution execution, LlmModelResolver models, AiOperations operations,
    TimeProvider time)
{
    public const string Operation = "application-answer";

    public Task<ApplicationAnswerGenerationResult> GenerateAsync(int listingId, string? model, AppDbContext db,
        CancellationToken ct, Guid? requestId = null) =>
        operations.RunAsync("generate-application-answers", new { listingId, model }, requestId, db,
            () => models.ResolveAsync(model, db, ct), resolved => GenerateCoreAsync(listingId, resolved, db, ct),
            result => new AiOutcome(result.Status == ApplicationAnswerGenerationStatus.Succeeded, result.ErrorCode), ct);

    private async Task<ApplicationAnswerGenerationResult> GenerateCoreAsync(int listingId, ResolvedLlmModel model,
        AppDbContext db, CancellationToken ct)
    {
        var context = await ApplicationAnswerPrompt.BuildAsync(listingId, db, time, ct);
        List<ChatMessage> messages =
            [new(ChatRole.System, context.Instructions), new(ChatRole.User, context.Data)];
        var first = await AskAsync(model, messages, context.Limits, db, ct);
        var over = first.Candidates?.Where(c => c.OverLimit).ToList();
        if (over is not { Count: > 0 })
            return first;

        // Models can't count characters, so tell them the measured lengths once; per question, the shorter answer wins.
        messages.Add(new(ChatRole.Assistant, JsonSerializer.Serialize(new
        {
            answers = first.Candidates!.Select(c => new { questionId = c.QuestionId, answer = c.Answer }),
        })));
        messages.Add(new(ChatRole.User, ApplicationAnswerPrompt.Shorten(over, context.Limits)));
        var second = await AskAsync(model, messages,
            over.ToDictionary(c => c.QuestionId, c => context.Limits[c.QuestionId]), db, ct);
        if (second.Candidates is not { } shorter)
            return first;
        var byId = shorter.ToDictionary(c => c.QuestionId);
        return first with
        {
            Candidates = first.Candidates!
                .Select(c => byId.TryGetValue(c.QuestionId, out var s) && s.Length < c.Length ? s : c).ToList(),
        };
    }

    private async Task<ApplicationAnswerGenerationResult> AskAsync(ResolvedLlmModel model, List<ChatMessage> messages,
        IReadOnlyDictionary<int, int?> expected, AppDbContext db, CancellationToken ct)
    {
        var call = await execution.RunJsonAsync(model, messages,
            new ChatOptions { ResponseFormat = ChatResponseFormat.Json, Temperature = 0.7f, MaxOutputTokens = 16384 },
            Operation, db, text => Decode(text, expected),
            "The AI's answers couldn't be read. Try again, or use the external prompt.", ct);
        return new(call.Value is null ? ApplicationAnswerGenerationStatus.Failed : ApplicationAnswerGenerationStatus.Succeeded,
            call.Value, call.Error, call.ErrorCode);
    }

    private static IReadOnlyList<ApplicationAnswerCandidate>? Decode(string text, IReadOnlyDictionary<int, int?> expected)
    {
        try { return ApplicationAnswerPrompt.Decode(text, expected, external: false); }
        catch (InvalidApplicationQuestionException) { return null; }
    }
}

public record ApplicationAnswerExternalPrompt(int Version, string Instructions);

[QueryType]
public static class ApplicationAnswerExternalPromptQueries
{
    public static async Task<ApplicationAnswerExternalPrompt> GetApplicationAnswerExternalPromptAsync(
        int listingId, AppDbContext db, TimeProvider time, CancellationToken ct)
    {
        ApplicationAnswerPromptContext context;
        try { context = await ApplicationAnswerPrompt.BuildAsync(listingId, db, time, ct); }
        // Typed errors are a mutation convention; a query reports them as GraphQL errors.
        catch (Exception e) when (e is ListingNotFoundException or InvalidApplicationQuestionException
                                      or InvalidCoverLetterException)
        {
            throw new GraphQLException(e.Message);
        }
        var prompt = $$"""
            {{context.Instructions}}
            Return it in a single ```json code block without prose, and include "schemaVersion": {{ApplicationAnswerPrompt.Version}} in the object.

            Listing, candidate, writing sample and question data:
            {{context.Data}}
            """;
        return new(ApplicationAnswerPrompt.Version, prompt);
    }
}

[MutationType]
public static class ApplicationAnswerGenerationMutations
{
    // InvalidApplicationQuestionError: no questions, or too much question text to answer together.
    // InvalidCoverLetterError: the shared listing and candidate context is over its size limit.
    [Error<ListingNotFoundException>]
    [Error<InvalidApplicationQuestionException>]
    [Error<InvalidCoverLetterException>]
    [Error<UnknownLlmModelException>]
    [Error<AiSafetyException>]
    [UseMutationConvention(PayloadFieldName = "result")]
    public static Task<ApplicationAnswerGenerationResult> GenerateApplicationAnswersAsync(int listingId,
        string? model, AppDbContext db, ApplicationAnswerGenerator generator, CancellationToken ct,
        Guid? requestId = null) =>
        generator.GenerateAsync(listingId, model, db, ct, requestId);

    // Validates answers pasted back from an external chat without saving them. Over the limit is flagged, not rejected.
    [Error<ListingNotFoundException>]
    [Error<InvalidApplicationQuestionException>]
    [UseMutationConvention(PayloadFieldName = "candidates")]
    public static async Task<IReadOnlyList<ApplicationAnswerCandidate>> PreviewApplicationAnswersAsync(int listingId,
        string json, AppDbContext db, CancellationToken ct)
    {
        if (!await db.Listings.AnyAsync(l => l.Id == listingId, ct))
            throw new ListingNotFoundException(listingId);
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidApplicationQuestionException("json: paste the answers object.");
        var limits = await db.ApplicationQuestions.Where(q => q.ListingId == listingId)
            .ToDictionaryAsync(q => q.Id, q => q.CharLimit, ct);
        if (limits.Count == 0)
            throw new InvalidApplicationQuestionException("questions: add a question first.");
        return ApplicationAnswerPrompt.Decode(json, limits, external: true);
    }
}
