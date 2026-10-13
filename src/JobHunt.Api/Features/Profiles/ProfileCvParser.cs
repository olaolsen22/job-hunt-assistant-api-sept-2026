using Google.GenAI;
using JobHunt.Api.Data;
using JobHunt.Api.Features.LlmModels;
using JobHunt.Api.Features.Parsing;
using JobHunt.Api.Llm;
using Microsoft.Extensions.AI;

namespace JobHunt.Api.Features.Profiles;

public enum ProfileCvParseStatus { Succeeded, Failed }
public record ProfileCvParseResult(ProfileCvParseStatus Status, ProfileCvCandidate? Candidate, string? Error);

// CV input and unreviewed output are transient. Only usage metadata is saved.
public class ProfileCvParser(
    [FromKeyedServices(GeminiOptions.Provider)] IChatClient chat,
    LlmModelResolver models, TimeProvider time)
{
    public const string Operation = "parse-profile-cv";
    public const int MaxTextLength = 100_000;

    public async Task<ProfileCvParseResult> ParseAsync(string text, string? model, AppDbContext db, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > MaxTextLength)
            throw new InvalidParseRequestException($"Paste English CV text (1–{MaxTextLength} characters).");
        var resolved = (await models.ResolveAsync(model, db, ct)).Id;

        var usage = new LlmUsage
        {
            UserId = db.CurrentUserId!, Provider = GeminiOptions.Provider, Model = resolved,
            Operation = Operation, CreatedAt = time.GetUtcNow().UtcDateTime,
        };
        db.LlmUsages.Add(usage);
        var started = time.GetTimestamp();
        ProfileCvParseResult result;
        try
        {
            var response = await chat.GetResponseAsync(
                [new(ChatRole.System, ProfileCvPromptQueries.Instructions), new(ChatRole.User, $"<cv>\n{text}\n</cv>")],
                new ChatOptions { ModelId = resolved, ResponseFormat = ChatResponseFormat.Json, Temperature = 0, MaxOutputTokens = 32_768 }, ct);
            usage.InputTokens = response.Usage?.InputTokenCount;
            usage.OutputTokens = response.Usage?.OutputTokenCount is { } output
                ? output + (response.Usage.ReasoningTokenCount ?? 0) : null;
            var candidate = ProfileCvDecoder.Decode(response.Text);
            result = new(ProfileCvParseStatus.Succeeded, candidate, null);
            usage.Success = true;
        }
        catch (InvalidProfileImportException)
        {
            usage.ErrorCode = "invalid-json";
            result = new(ProfileCvParseStatus.Failed, null, "The AI's answer couldn't be read. Try again, or import a smaller CV section.");
        }
        catch (HttpRequestException ex)
        {
            var limited = ex is ApiException { StatusCode: 429 } || ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests;
            usage.ErrorCode = limited ? "rate-limited" : "provider-error";
            result = new(ProfileCvParseStatus.Failed, null, limited
                ? "The AI's free-tier limit was reached. Try again in a minute."
                : "The AI provider couldn't be reached. Try again later.");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            usage.ErrorCode = "timeout";
            result = new(ProfileCvParseStatus.Failed, null, "The AI took too long to answer. Try again.");
        }
        usage.LatencyMs = (long)time.GetElapsedTime(started).TotalMilliseconds;
        await db.SaveChangesAsync(ct);
        return result;
    }
}

[MutationType]
public static class ProfileCvParseMutations
{
    [Error<InvalidParseRequestException>]
    [Error<UnknownLlmModelException>]
    [UseMutationConvention(PayloadFieldName = "result")]
    public static Task<ProfileCvParseResult> ParseProfileCvAsync(
        string text, string? model, AppDbContext db, ProfileCvParser parser, CancellationToken ct) =>
        parser.ParseAsync(text, model, db, ct);
}
