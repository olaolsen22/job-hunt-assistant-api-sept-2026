using JobHunt.Api.Data;
using JobHunt.Api.Llm;

namespace JobHunt.Api.Features.Parsing;

public class InvalidParseRequestException(string message) : Exception(message);

[MutationType]
public static class ParseMutations
{
    // Well above any single ad; the parser only sends the first 12k characters anyway.
    private const int MaxTextLength = 100_000;

    // Returns the finished job for now. With the queue it will return QUEUED and finish over onParseJobUpdated,
    // so the UI already handles both.
    [Error<InvalidParseRequestException>]
    public static async Task<ParseJob> ParseListingAsync(
        string text, ParseMode mode,
        AppDbContext db, ListingParser parser, TimeProvider time, CancellationToken ct)
    {
        text = text.Trim();
        if (text.Length == 0)
            throw new InvalidParseRequestException("Paste the job ad text first.");
        if (text.Length > MaxTextLength)
            throw new InvalidParseRequestException($"The text is longer than {MaxTextLength} characters.");

        var now = time.GetUtcNow().UtcDateTime;
        var job = new ParseJob
        {
            UserId = db.CurrentUserId!,
            Mode = mode,
            Status = ParseJobStatus.Running,
            RawText = text,
            Provider = GeminiOptions.Provider,
            CreatedAt = now,
            StartedAt = now,
        };
        db.ParseJobs.Add(job);
        await parser.RunAsync(job, db, ct);
        await db.SaveChangesAsync(ct);
        return job;
    }
}
