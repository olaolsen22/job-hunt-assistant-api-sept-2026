using System.Text.Json;
using JobHunt.Api.Data;
using JobHunt.Api.Features.AiSafeguards;
using JobHunt.Api.Features.CoverLetters;
using JobHunt.Api.Features.LlmModels;
using JobHunt.Api.Llm;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;

namespace JobHunt.Api.Features.ApplicationQuestions;

public enum ApplicationAnswerGenerationStatus { Succeeded, Failed }

public record ApplicationAnswerGenerationResult(ApplicationAnswerGenerationStatus Status,
    ApplicationAnswerCandidate? Candidate, string? Error, string? ErrorCode);

public class ApplicationAnswerGenerator(LlmExecution execution, LlmModelResolver models, AiOperations operations,
    TimeProvider time)
{
    public const string Operation = "application-answer";

    public Task<ApplicationAnswerGenerationResult> GenerateAsync(int questionId, string? model, AppDbContext db,
        CancellationToken ct, Guid? requestId = null) =>
        operations.RunAsync("generate-application-answer", new { questionId, model }, requestId, db,
            () => models.ResolveAsync(model, db, ct), resolved => GenerateCoreAsync(questionId, resolved, db, ct),
            result => new AiOutcome(result.Status == ApplicationAnswerGenerationStatus.Succeeded, result.ErrorCode), ct);

    private async Task<ApplicationAnswerGenerationResult> GenerateCoreAsync(int questionId, ResolvedLlmModel model,
        AppDbContext db, CancellationToken ct)
    {
        var context = await ApplicationAnswerPrompt.BuildAsync(questionId, db, time, ct);
        List<ChatMessage> messages =
            [new(ChatRole.System, context.Instructions), new(ChatRole.User, context.Data)];
        var first = await AskAsync(model, messages, context.CharLimit, db, ct);
        if (first.Candidate is not { OverLimit: true } over)
            return first;

        // Models can't count characters; told the measured length once, they usually land under the limit.
        // If the retry fails or still runs over, the user gets the best answer flagged and trims it.
        messages.Add(new(ChatRole.Assistant, JsonSerializer.Serialize(new { answer = over.Answer })));
        messages.Add(new(ChatRole.User, ApplicationAnswerPrompt.Shorten(over.Length, context.CharLimit!.Value)));
        var second = await AskAsync(model, messages, context.CharLimit, db, ct);
        return second.Candidate is { } shorter && shorter.Length < over.Length ? second : first;
    }

    private async Task<ApplicationAnswerGenerationResult> AskAsync(ResolvedLlmModel model, List<ChatMessage> messages,
        int? charLimit, AppDbContext db, CancellationToken ct)
    {
        var call = await execution.RunAsync(model, messages,
            new ChatOptions { ResponseFormat = ChatResponseFormat.Json, Temperature = 0.7f, MaxOutputTokens = 8192 },
            Operation, db, ct);

        ApplicationAnswerGenerationResult result;
        if (call.Error is { } error)
            result = new(ApplicationAnswerGenerationStatus.Failed, null, error, call.ErrorCode);
        else
        {
            try
            {
                result = new(ApplicationAnswerGenerationStatus.Succeeded,
                    ApplicationAnswerPrompt.Decode(call.Response!.Text, charLimit, external: false), null, null);
            }
            catch (InvalidApplicationQuestionException)
            {
                result = new(ApplicationAnswerGenerationStatus.Failed, null,
                    "The AI's answer couldn't be read. Try again, or use the external prompt.", "invalid-json");
            }
        }
        if (call.Usage is { } usage)
        {
            if (result.Status != ApplicationAnswerGenerationStatus.Succeeded)
            {
                usage.Success = false;
                usage.ErrorCode ??= "invalid-json";
            }
            await execution.SaveUsageAsync(usage, ct);
        }
        return result;
    }
}

public record ApplicationAnswerExternalPrompt(int Version, string Instructions);

[QueryType]
public static class ApplicationAnswerExternalPromptQueries
{
    [Error<ApplicationQuestionNotFoundException>]
    [Error<InvalidCoverLetterException>]
    public static async Task<ApplicationAnswerExternalPrompt> GetApplicationAnswerExternalPromptAsync(
        int questionId, AppDbContext db, TimeProvider time, CancellationToken ct)
    {
        var context = await ApplicationAnswerPrompt.BuildAsync(questionId, db, time, ct);
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
    // InvalidCoverLetterError: the shared listing and candidate context is over its size limit.
    [Error<ApplicationQuestionNotFoundException>]
    [Error<InvalidCoverLetterException>]
    [Error<UnknownLlmModelException>]
    [Error<AiSafetyException>]
    [UseMutationConvention(PayloadFieldName = "result")]
    public static Task<ApplicationAnswerGenerationResult> GenerateApplicationAnswerAsync(int questionId,
        string? model, AppDbContext db, ApplicationAnswerGenerator generator, CancellationToken ct,
        Guid? requestId = null) =>
        generator.GenerateAsync(questionId, model, db, ct, requestId);

    // Validates an answer pasted back from an external chat without saving it. Over the limit is flagged, not rejected.
    [Error<ApplicationQuestionNotFoundException>]
    [Error<InvalidApplicationQuestionException>]
    [UseMutationConvention(PayloadFieldName = "candidate")]
    public static async Task<ApplicationAnswerCandidate> PreviewApplicationAnswerAsync(int questionId, string json,
        AppDbContext db, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidApplicationQuestionException("json: paste the answer object.");
        var question = await db.ApplicationQuestions.Where(q => q.Id == questionId)
            .Select(q => new { q.CharLimit }).FirstOrDefaultAsync(ct)
            ?? throw new ApplicationQuestionNotFoundException(questionId);
        return ApplicationAnswerPrompt.Decode(json, question.CharLimit, external: true);
    }
}
