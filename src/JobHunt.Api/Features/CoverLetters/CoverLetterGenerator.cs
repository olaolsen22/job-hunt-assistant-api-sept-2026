using JobHunt.Api.Data;
using JobHunt.Api.Features.AiSafeguards;
using JobHunt.Api.Features.Listings;
using JobHunt.Api.Features.LlmModels;
using JobHunt.Api.Llm;
using Microsoft.Extensions.AI;

namespace JobHunt.Api.Features.CoverLetters;

public enum CoverLetterGenerationStatus { Succeeded, Failed }

// Content is an unsaved candidate: the UI shows it for review and saves it with saveCoverLetterDraft.
public record CoverLetterGenerationResult(CoverLetterGenerationStatus Status, CoverLetterContent? Content,
    string? Error, string? ErrorCode);

public class CoverLetterGenerator(LlmExecution execution, LlmModelResolver models, AiOperations operations,
    TimeProvider time)
{
    public const string Operation = "cover-letter";

    public Task<CoverLetterGenerationResult> GenerateAsync(int listingId, string? model, AppDbContext db,
        CancellationToken ct, Guid? requestId = null) =>
        operations.RunAsync("generate-cover-letter", new { listingId, model }, requestId, db,
            () => models.ResolveAsync(model, db, ct), resolved => GenerateCoreAsync(listingId, resolved, db, ct),
            result => new AiOutcome(result.Status == CoverLetterGenerationStatus.Succeeded, result.ErrorCode), ct);

    private async Task<CoverLetterGenerationResult> GenerateCoreAsync(int listingId, ResolvedLlmModel model,
        AppDbContext db, CancellationToken ct)
    {
        var context = await CoverLetterContext.BuildAsync(listingId, db, time, ct);
        var call = await execution.RunJsonAsync(model,
            [new(ChatRole.System, CoverLetterPrompt.Instructions(context.Language, context.Kind)),
             new(ChatRole.User, $"<cover_letter_context>\n{context.Json}\n</cover_letter_context>")],
            new ChatOptions { ResponseFormat = ChatResponseFormat.Json, Temperature = 0.7f, MaxOutputTokens = 4096 },
            Operation, db, text => Decode(text, context),
            "The AI's answer couldn't be read. Try again, or use the external prompt.", ct);
        return new(call.Value is null ? CoverLetterGenerationStatus.Failed : CoverLetterGenerationStatus.Succeeded,
            call.Value, call.Error, call.ErrorCode);
    }

    private static CoverLetterContent? Decode(string text, CoverLetterPromptContext context)
    {
        try { return CoverLetterPrompt.Decode(text, context, external: false); }
        catch (InvalidCoverLetterException) { return null; }
    }
}

public record CoverLetterExternalPrompt(int Version, string Instructions);

[QueryType]
public static class CoverLetterExternalPromptQueries
{
    public static async Task<CoverLetterExternalPrompt> GetCoverLetterExternalPromptAsync(
        int listingId, AppDbContext db, TimeProvider time, CancellationToken ct)
    {
        CoverLetterPromptContext context;
        try { context = await CoverLetterContext.BuildAsync(listingId, db, time, ct); }
        // Typed errors are a mutation convention; a query reports them as GraphQL errors.
        catch (Exception e) when (e is ListingNotFoundException or InvalidCoverLetterException)
        {
            throw new GraphQLException(e.Message);
        }
        var prompt = $$"""
            {{CoverLetterPrompt.Instructions(context.Language, context.Kind)}}
            Return it in a single ```json code block without prose, and include "schemaVersion": {{CoverLetterPrompt.Version}} in the object.

            Listing, candidate, and writing sample data:
            <cover_letter_context>
            {{context.Json}}
            </cover_letter_context>
            """;
        return new(CoverLetterPrompt.Version, prompt);
    }
}

[MutationType]
public static class CoverLetterGenerationMutations
{
    [Error<ListingNotFoundException>]
    [Error<InvalidCoverLetterException>]
    [Error<UnknownLlmModelException>]
    [Error<AiSafetyException>]
    [UseMutationConvention(PayloadFieldName = "result")]
    public static Task<CoverLetterGenerationResult> GenerateCoverLetterAsync(int listingId, string? model,
        AppDbContext db, CoverLetterGenerator generator, CancellationToken ct, Guid? requestId = null) =>
        generator.GenerateAsync(listingId, model, db, ct, requestId);

    // Validates a letter pasted back from an external chat without saving it.
    [Error<ListingNotFoundException>]
    [Error<InvalidCoverLetterException>]
    [UseMutationConvention(PayloadFieldName = "candidate")]
    public static async Task<CoverLetterContent> PreviewCoverLetterDraftAsync(int listingId, string json,
        AppDbContext db, TimeProvider time, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidCoverLetterException("json: paste the cover letter object.");
        var context = await CoverLetterContext.BuildAsync(listingId, db, time, ct);
        return CoverLetterPrompt.Decode(json, context, external: true);
    }

    // The no-AI path: a letter written elsewhere, pasted whole and split by the server.
    [Error<ListingNotFoundException>]
    [Error<InvalidCoverLetterException>]
    [UseMutationConvention(PayloadFieldName = "candidate")]
    public static async Task<CoverLetterContent> PreviewCoverLetterTextAsync(int listingId, string text,
        AppDbContext db, TimeProvider time, CancellationToken ct) =>
        CoverLetterTextParser.Parse(text, await CoverLetterContext.HeaderAsync(listingId, db, time, ct));
}
