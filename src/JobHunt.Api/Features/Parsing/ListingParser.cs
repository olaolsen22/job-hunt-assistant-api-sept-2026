using System.Text.Json;
using JobHunt.Api.Data;
using JobHunt.Api.Features.Companies;
using JobHunt.Api.Features.Listings;
using JobHunt.Api.Llm;
using Microsoft.Extensions.AI;

namespace JobHunt.Api.Features.Parsing;

// Runs one parse job. parseListing calls it inline for now; the background queue will call it later.
public class ListingParser(
    LlmExecution execution,
    TimeProvider time)
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
    public async Task RunAsync(ParseJob job, ResolvedLlmModel selection, AppDbContext db, CancellationToken ct)
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
            ModelId = job.Model,
            // JSON mode: the model can't wrap the answer in prose or code fences.
            ResponseFormat = ChatResponseFormat.Json,
            Temperature = 0,
            MaxOutputTokens = job.Mode == ParseMode.Quick ? QuickMaxOutputTokens : MaxOutputTokens,
        };
        // Deep analysis of one ad benefits from thinking; quick-add arrays would burn their output budget on it.
        if (job.Mode == ParseMode.Full)
            options.Reasoning = new ReasoningOptions { Effort = ReasoningEffort.Low };

        var call = await execution.RunAsync(selection, messages, options,
            job.Mode == ParseMode.Companies ? CompaniesOperation : Operation, db, ct);
        if (call.Error is { } error)
        {
            job.Status = ParseJobStatus.Failed;
            job.Error = error;
        }
        else if (IsExpectedJson(call.Response!.Text, job.Mode))
        {
            job.Status = ParseJobStatus.Succeeded;
            job.Result = call.Response.Text;
        }
        else
        {
            job.Status = ParseJobStatus.Failed;
            job.Error = "The AI's answer couldn't be read. Try again, or use copy-paste.";
            call.Usage!.Success = false;
            call.Usage.ErrorCode = "invalid-json";
        }
        if (call.Usage is { } audited) await execution.SaveUsageAsync(audited, ct);
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
