using JobHunt.Api.Data;
using JobHunt.Api.Features.AiSafeguards;
using JobHunt.Api.Features.Listings;
using JobHunt.Api.Features.LlmModels;
using JobHunt.Api.Llm;
using Microsoft.EntityFrameworkCore;

namespace JobHunt.Api.Features.Parsing;

public class InvalidParseRequestException(string message) : Exception(message);

[MutationType]
public static class ParseMutations
{
    // Well above any single ad; quick add has its own limit for whole pasted search pages.
    private const int MaxTextLength = 100_000;

    // Returns the finished job for now. With the queue it will return QUEUED and finish over onParseJobUpdated,
    // so the UI already handles both.
    [Error<InvalidParseRequestException>]
    [Error<UnknownLlmModelException>]
    [Error<AiSafetyException>]
    public static async Task<ParseJob> ParseListingAsync(
        string text, ParseMode mode, string? model,
        AppDbContext db, ListingParser parser, LlmModelResolver models, AiOperations operations, TimeProvider time, CancellationToken ct, Guid? requestId = null)
    {
        text = text.Trim();
        if (text.Length == 0)
            throw new InvalidParseRequestException("Paste the job ad text first.");
        var maxLength = mode == ParseMode.Quick ? ListingParser.QuickMaxInputChars : MaxTextLength;
        if (text.Length > maxLength)
            throw new InvalidParseRequestException($"The text is longer than {maxLength} characters.");

        return await operations.RunAsync(ListingParser.Operation, new { text, mode, model }, requestId, db,
            () => models.ResolveAsync(model, db, ct), resolved => RunJobAsync(text, mode, resolved, db, parser, time, ct),
            Outcome, ct);
    }

    // Full mode only: the owner is analyzing one saved listing. The URL comes from the listing, never the client, so
    // the API stays the source of truth for it. The text is what the model sees, so it's stored on the job like
    // pasted text.
    [Error<ListingNotFoundException>]
    [Error<InvalidParseRequestException>]
    [Error<UnknownLlmModelException>]
    [Error<AiSafetyException>]
    public static async Task<ParseJob> ParseListingFromUrlAsync(
        int listingId, string? model, AppDbContext db, ListingParser parser, LlmModelResolver models,
        ListingPageFetcher fetcher, AiOperations operations, TimeProvider time, CancellationToken ct, Guid? requestId = null)
    {
        string? url = null;
        return await operations.RunAsync("parse-listing-from-url", new { listingId, model }, requestId, db,
            async () =>
            {
                // Model validation retains its precedence; historical receipt replay does not re-fetch sources.
                var resolved = await models.ResolveAsync(model, db, ct);
                var listing = await db.Listings.Where(l => l.Id == listingId).Select(l => new { l.Url })
                    .FirstOrDefaultAsync(ct) ?? throw new ListingNotFoundException(listingId);
                url = listing.Url ?? throw new InvalidParseRequestException("This application has no page to analyze.");
                return resolved;
            }, async resolved =>
            {
                var text = await fetcher.GetListingTextAsync(new Uri(url!), ct);
                return await RunJobAsync(text, ParseMode.Full, resolved, db, parser, time, ct);
            }, Outcome, ct);
    }

    // For a listing that isn't saved yet, or a company page (COMPANIES: its about, team or contact page), so the URL
    // has to come from the client. The fetcher's public-address check is what keeps this from reaching internal hosts.
    [Error<InvalidParseRequestException>]
    [Error<UnknownLlmModelException>]
    [Error<AiSafetyException>]
    public static async Task<ParseJob> ParseUrlAsync(
        string url, string? model, AppDbContext db, ListingParser parser, LlmModelResolver models,
        ListingPageFetcher fetcher, AiOperations operations, TimeProvider time, CancellationToken ct,
        Guid? requestId = null, ParseMode? mode = null)
    {
        // Nullable rather than defaulted: Hot Chocolate can't print an enum default on a mutation-convention input.
        var parseMode = mode ?? ParseMode.Full;
        // Quick add reads a pasted search page, which a fetched page isn't.
        if (parseMode == ParseMode.Quick)
            throw new InvalidParseRequestException("Quick add works on pasted text, not a link.");
        url = url.Trim();
        if (!Inputs.TryHttpUrl(url, out var uri))
            throw new InvalidParseRequestException(parseMode == ParseMode.Companies
                ? "Enter a full http(s) link to the company's page."
                : "Enter a full http(s) link to the job ad.");

        return await operations.RunAsync("parse-url", new { url, model, mode = parseMode }, requestId, db,
            () => models.ResolveAsync(model, db, ct), async resolved =>
            {
                var text = parseMode == ParseMode.Companies
                    ? await fetcher.GetCompanyTextAsync(uri, ct)
                    : await fetcher.GetListingTextAsync(uri, ct);
                return await RunJobAsync(text, parseMode, resolved, db, parser, time, ct);
            }, Outcome, ct);
    }

    private static AiOutcome Outcome(ParseJob job) => new(job.Status == ParseJobStatus.Succeeded,
        job.Status == ParseJobStatus.Failed ? "parse-failed" : null, job);

    private static async Task<ParseJob> RunJobAsync(
        string text, ParseMode mode, ResolvedLlmModel model, AppDbContext db, ListingParser parser, TimeProvider time,
        CancellationToken ct)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var job = new ParseJob
        {
            UserId = db.CurrentUserId!,
            Mode = mode,
            Status = ParseJobStatus.Running,
            RawText = text,
            Provider = model.Provider,
            Model = model.ModelId,
            CreatedAt = now,
            StartedAt = now,
        };
        db.ParseJobs.Add(job);
        await parser.RunAsync(job, model, db, ct);
        return job;
    }
}
