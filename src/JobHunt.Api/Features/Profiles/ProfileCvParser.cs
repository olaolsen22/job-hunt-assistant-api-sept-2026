using JobHunt.Api.Data;
using JobHunt.Api.Features.AiSafeguards;
using JobHunt.Api.Features.LlmModels;
using JobHunt.Api.Features.Parsing;
using JobHunt.Api.Llm;
using Microsoft.Extensions.AI;

namespace JobHunt.Api.Features.Profiles;

public enum ProfileCvParseStatus { Succeeded, Failed }
public record ProfileCvParseResult(ProfileCvParseStatus Status, ProfileCvCandidate? Candidate, string? Error);

// CV input and unreviewed output are transient. Only usage metadata is saved.
public class ProfileCvParser(
    LlmExecution execution, LlmModelResolver models, AiOperations operations)
{
    public const string Operation = "parse-profile-cv";
    public const int MaxTextLength = 100_000;

    public async Task<ProfileCvParseResult> ParseAsync(string text, string? model, AppDbContext db, CancellationToken ct, Guid? requestId = null)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > MaxTextLength)
            throw new InvalidParseRequestException($"Paste English CV text (1–{MaxTextLength} characters).");
        return await operations.RunAsync("parse-profile-cv", new { text, model }, requestId, db,
            () => models.ResolveAsync(model, db, ct), resolved => ParseCoreAsync(text, resolved, db, ct),
            result => new AiOutcome(result.Status == ProfileCvParseStatus.Succeeded), ct);
    }

    private async Task<ProfileCvParseResult> ParseCoreAsync(string text, ResolvedLlmModel resolved, AppDbContext db, CancellationToken ct)
    {
        var call = await execution.RunJsonAsync(resolved,
            [new(ChatRole.System, await ProfileCvPromptQueries.InstructionsAsync(db, ct)), new(ChatRole.User, $"<cv>\n{text}\n</cv>")],
            new ChatOptions { ResponseFormat = ChatResponseFormat.Json, Temperature = 0, MaxOutputTokens = 32_768 },
            Operation, db, Decode, "The AI's answer couldn't be read. Try again, or import a smaller CV section.", ct);
        return call.Value is { } candidate
            ? new(ProfileCvParseStatus.Succeeded, candidate, null)
            : new(ProfileCvParseStatus.Failed, null, call.Error);
    }

    private static ProfileCvCandidate? Decode(string text)
    {
        try { return ProfileCvDecoder.Decode(text); }
        catch (InvalidProfileImportException) { return null; }
    }
}

[MutationType]
public static class ProfileCvParseMutations
{
    [Error<InvalidParseRequestException>]
    [Error<UnknownLlmModelException>]
    [Error<AiSafetyException>]
    [UseMutationConvention(PayloadFieldName = "result")]
    public static Task<ProfileCvParseResult> ParseProfileCvAsync(
        string text, string? model, AppDbContext db, ProfileCvParser parser, CancellationToken ct, Guid? requestId = null) =>
        parser.ParseAsync(text, model, db, ct, requestId);
}
