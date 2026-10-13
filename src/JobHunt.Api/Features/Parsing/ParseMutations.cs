using JobHunt.Api.Data;
using JobHunt.Api.Features.Listings;
using JobHunt.Api.Features.LlmModels;
using JobHunt.Api.Llm;
using Microsoft.EntityFrameworkCore;

namespace JobHunt.Api.Features.Parsing;

public class InvalidParseRequestException(string message) : Exception(message);

[MutationType]
public static class ParseMutations
{
    // Well above any single ad; the parser only sends the first 12k characters anyway.
    private const int MaxTextLength = 100_000;
    // Practical browser limit; real job ad links are far shorter.
    private const int MaxUrlLength = 2048;

    // Returns the finished job for now. With the queue it will return QUEUED and finish over onParseJobUpdated,
    // so the UI already handles both.
    [Error<InvalidParseRequestException>]
    [Error<UnknownLlmModelException>]
    public static async Task<ParseJob> ParseListingAsync(
        string text, ParseMode mode, string? model,
        AppDbContext db, ListingParser parser, LlmModelResolver models, TimeProvider time, CancellationToken ct)
    {
        var resolved = await models.ResolveAsync(model, db, ct);
        text = text.Trim();
        if (text.Length == 0)
            throw new InvalidParseRequestException("Paste the job ad text first.");
        if (text.Length > MaxTextLength)
            throw new InvalidParseRequestException($"The text is longer than {MaxTextLength} characters.");

        return await RunJobAsync(text, mode, resolved, db, parser, time, ct);
    }

    // Full mode only: the owner is analyzing one saved listing. The URL comes from the listing, never the client, so
    // the API stays the source of truth for it. The text is what the model sees, so it's stored on the job like
    // pasted text.
    [Error<ListingNotFoundException>]
    [Error<InvalidParseRequestException>]
    [Error<UnknownLlmModelException>]
    public static async Task<ParseJob> ParseListingFromUrlAsync(
        int listingId, string? model, AppDbContext db, ListingParser parser, LlmModelResolver models,
        ListingPageFetcher fetcher, TimeProvider time, CancellationToken ct)
    {
        // Before the fetch, so a rejected model costs no outbound request either.
        var resolved = await models.ResolveAsync(model, db, ct);
        var url = await db.Listings.Where(l => l.Id == listingId).Select(l => l.Url).FirstOrDefaultAsync(ct)
            ?? throw new ListingNotFoundException(listingId);

        var text = await fetcher.GetListingTextAsync(new Uri(url), ct);
        return await RunJobAsync(text, ParseMode.Full, resolved, db, parser, time, ct);
    }

    // For a listing that isn't saved yet, so the URL has to come from the client. The fetcher's public-address
    // check is what keeps this from reaching internal hosts.
    [Error<InvalidParseRequestException>]
    [Error<UnknownLlmModelException>]
    public static async Task<ParseJob> ParseUrlAsync(
        string url, string? model, AppDbContext db, ListingParser parser, LlmModelResolver models,
        ListingPageFetcher fetcher, TimeProvider time, CancellationToken ct)
    {
        var resolved = await models.ResolveAsync(model, db, ct);
        url = url.Trim();
        if (url.Length > MaxUrlLength
            || !Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new InvalidParseRequestException("Enter a full http(s) link to the job ad.");

        var text = await fetcher.GetListingTextAsync(uri, ct);
        return await RunJobAsync(text, ParseMode.Full, resolved, db, parser, time, ct);
    }

    private static async Task<ParseJob> RunJobAsync(
        string text, ParseMode mode, LlmModelOption model, AppDbContext db, ListingParser parser, TimeProvider time,
        CancellationToken ct)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var job = new ParseJob
        {
            UserId = db.CurrentUserId!,
            Mode = mode,
            Status = ParseJobStatus.Running,
            RawText = text,
            Provider = GeminiOptions.Provider,
            Model = model.Id,
            CreatedAt = now,
            StartedAt = now,
        };
        db.ParseJobs.Add(job);
        await parser.RunAsync(job, db, ct);
        await db.SaveChangesAsync(ct);
        return job;
    }
}
