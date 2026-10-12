using System.Text.Json;
using Google.GenAI;
using JobHunt.Api.Data;
using JobHunt.Api.Features.Companies;
using JobHunt.Api.Features.Listings;
using JobHunt.Api.Llm;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace JobHunt.Api.Features.Parsing;

// Runs one parse job. parseListing calls it inline for now; the background queue will call it later.
public class ListingParser(
    [FromKeyedServices(GeminiOptions.Provider)] IChatClient chat,
    IOptions<GeminiOptions> gemini,
    TimeProvider time,
    ILogger<ListingParser> logger)
{
    public const string Operation = "parse-listing";
    public const string CompaniesOperation = "parse-companies";

    // ≈3k tokens. Anything past this in a pasted ad is almost always footer and cookie text.
    private const int MaxInputChars = 12_000;

    // A full listing needs far less output than this.
    private const int MaxOutputTokens = 8_192;

    // Quick add is one call for a whole pasted search page (~150 rows at ~130 output tokens each).
    // Flash-Lite takes 1M input and 65k output tokens, so these only guard against runaway pastes.
    private const int QuickMaxInputChars = 200_000;
    private const int QuickMaxOutputTokens = 32_768;

    // Sets the job's outcome and adds an LlmUsage row; the caller saves both.
    public async Task RunAsync(ParseJob job, AppDbContext db, CancellationToken ct)
    {
        // The same prompts the copy-paste path serves, so both paths return the same JSON.
        var instructions = job.Mode switch
        {
            ParseMode.Full => (await ListingParsePromptQueries.GetListingParsePromptAsync(db, time, ct)).Instructions,
            ParseMode.Quick => (await ListingParsePromptQueries.GetListingQuickParsePromptAsync(db, time, ct)).Instructions,
            _ => (await CompanyParsePromptQueries.GetCompanyParsePromptAsync(db, ct)).Instructions,
        };
        var maxInputChars = job.Mode == ParseMode.Quick ? QuickMaxInputChars : MaxInputChars;
        var posting = job.RawText.Length > maxInputChars ? job.RawText[..maxInputChars] : job.RawText;
        List<ChatMessage> messages =
        [
            new(ChatRole.System, instructions),
            new(ChatRole.User, $"<posting>\n{posting}\n</posting>"),
        ];
        var options = new ChatOptions
        {
            // JSON mode: the model can't wrap the answer in prose or code fences.
            ResponseFormat = ChatResponseFormat.Json,
            Temperature = 0,
            MaxOutputTokens = job.Mode == ParseMode.Quick ? QuickMaxOutputTokens : MaxOutputTokens,
        };
        // Deep analysis of one ad benefits from thinking; quick-add arrays would burn their output budget on it.
        if (job.Mode == ParseMode.Full)
            options.Reasoning = new ReasoningOptions { Effort = ReasoningEffort.Low };

        var usage = new LlmUsage
        {
            UserId = job.UserId,
            Provider = job.Provider,
            Model = gemini.Value.Model,
            Operation = job.Mode == ParseMode.Companies ? CompaniesOperation : Operation,
            CreatedAt = time.GetUtcNow().UtcDateTime,
        };
        db.LlmUsages.Add(usage);

        var started = time.GetTimestamp();
        try
        {
            var response = await chat.GetResponseAsync(messages, options, ct);
            usage.InputTokens = response.Usage?.InputTokenCount;
            // Gemini bills thinking as output but reports it separately.
            usage.OutputTokens = response.Usage?.OutputTokenCount is { } output
                ? output + (response.Usage.ReasoningTokenCount ?? 0)
                : null;
            if (IsExpectedJson(response.Text, job.Mode))
            {
                job.Status = ParseJobStatus.Succeeded;
                job.Result = response.Text;
                usage.Success = true;
            }
            else
            {
                // Also what a reply cut off at MaxOutputTokens looks like.
                job.Status = ParseJobStatus.Failed;
                job.Error = "The AI's answer couldn't be read. Try again, or use copy-paste.";
                usage.ErrorCode = "invalid-json";
            }
        }
        // Google's ApiException derives from HttpRequestException, so this covers API errors and network failures.
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "LLM call for parse job {ParseJobId} failed", job.Id);
            var rateLimited = ex is ApiException { StatusCode: 429 };
            job.Status = ParseJobStatus.Failed;
            job.Error = rateLimited
                ? "The AI's free-tier limit was reached. Try again in a minute."
                : "The AI provider couldn't be reached. Try again later.";
            usage.ErrorCode = rateLimited ? "rate-limited" : "provider-error";
        }
        // HttpClient reports its own timeout as TaskCanceledException; with our token still live it can't be the
        // client hanging up, so it's the provider being too slow.
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "LLM call for parse job {ParseJobId} timed out", job.Id);
            job.Status = ParseJobStatus.Failed;
            job.Error = "The AI took too long to answer. Try again.";
            usage.ErrorCode = "timeout";
        }
        usage.LatencyMs = (long)time.GetElapsedTime(started).TotalMilliseconds;
        job.CompletedAt = time.GetUtcNow().UtcDateTime;
    }

    private static bool IsExpectedJson(string text, ParseMode mode)
    {
        try
        {
            using var json = JsonDocument.Parse(text);
            return json.RootElement.ValueKind == (mode == ParseMode.Full ? JsonValueKind.Object : JsonValueKind.Array);
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
