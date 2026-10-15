using System.Security.Cryptography;
using JobHunt.Api.Data;
using JobHunt.Api.Features.Documents;
using JobHunt.Api.Features.Listings;
using Microsoft.EntityFrameworkCore;

namespace JobHunt.Api.Features.CoverLetters;

// A null Language means the listing's posting language, or English when the listing has none.
public record CoverLetterContentInput(Language? Language = null, string? FullName = null, string? Address = null,
    string? Phone = null, string? Email = null, DateOnly? Date = null, string? Organization = null,
    string? RecipientName = null, string? RecipientAddress = null, string? Subject = null, string? Greeting = null,
    List<string>? Paragraphs = null, string? Closing = null);

public class CoverLetterDraftNotFoundException(int listingId)
    : Exception($"No cover letter draft was found for listing {listingId}.");

public class InvalidCoverLetterException(string message) : Exception(message);

[QueryType]
public static class CoverLetterDraftQueries
{
    public static Task<CoverLetterDraft?> GetCoverLetterDraftAsync(int listingId, AppDbContext db, CancellationToken ct) =>
        db.CoverLetterDrafts.AsNoTracking().FirstOrDefaultAsync(d => d.ListingId == listingId, ct);

    // Starting point for a new or reset letter: sender, date, recipient, subject and sign-off with no body.
    // Nothing is saved; null for an unknown listing.
    public static async Task<CoverLetterContent?> GetCoverLetterDraftFromProfileAsync(int listingId, AppDbContext db,
        TimeProvider time, CancellationToken ct)
    {
        try { return await CoverLetterContext.HeaderAsync(listingId, db, time, ct); }
        catch (ListingNotFoundException) { return null; }
    }
}

[MutationType]
public static class CoverLetterDraftMutations
{
    // Full replace of content and brief. The brief can be saved before any content exists, then generation reads it.
    [Error<ListingNotFoundException>]
    [Error<InvalidCoverLetterException>]
    public static async Task<CoverLetterDraft> SaveCoverLetterDraftAsync(int listingId, CoverLetterContentInput content,
        string? brief, AppDbContext db, TimeProvider time, CancellationToken ct)
    {
        var listing = await db.Listings.Where(l => l.Id == listingId)
            .Select(l => new { l.PostingLanguage }).FirstOrDefaultAsync(ct)
            ?? throw new ListingNotFoundException(listingId);
        var normalized = CoverLetterValidation.Normalize(content, listing.PostingLanguage);
        brief = CoverLetterValidation.Text(brief, "brief", CoverLetterValidation.MaxBriefLength);

        var draft = await db.CoverLetterDrafts.FirstOrDefaultAsync(d => d.ListingId == listingId, ct);
        var now = time.GetUtcNow().UtcDateTime;
        if (draft is null)
        {
            draft = new CoverLetterDraft { UserId = db.CurrentUserId!, ListingId = listingId, CreatedAt = now };
            db.CoverLetterDrafts.Add(draft);
        }
        draft.Content = normalized;
        draft.Brief = brief;
        draft.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        return draft;
    }

    // Each generation preserves existing documents; remove old ones with deleteDocument.
    [Error<ListingNotFoundException>]
    [Error<CoverLetterDraftNotFoundException>]
    public static async Task<Document> GenerateCoverLetterDocumentAsync(
        int listingId, AppDbContext db, TimeProvider time, CancellationToken ct)
    {
        var listing = await db.Listings
            .Where(l => l.Id == listingId)
            .Select(l => new { Company = l.Company.Name, JobRole = l.JobRole.Name })
            .FirstOrDefaultAsync(ct)
            ?? throw new ListingNotFoundException(listingId);
        var draft = await db.CoverLetterDrafts.AsNoTracking().FirstOrDefaultAsync(d => d.ListingId == listingId, ct)
            ?? throw new CoverLetterDraftNotFoundException(listingId);

        var bytes = CoverLetterPdfRenderer.Render(draft.Content);
        var now = time.GetUtcNow().UtcDateTime;
        var fileName = DocumentMutations.FileName(listing.Company, listing.JobRole, DocumentKind.CoverLetter, now);
        var document = new Document
        {
            UserId = db.CurrentUserId!,
            ListingId = listingId,
            Kind = DocumentKind.CoverLetter,
            FileName = fileName,
            OriginalFileName = fileName,
            SizeBytes = bytes.Length,
            ContentHash = Convert.ToHexStringLower(SHA256.HashData(bytes)),
            CreatedAt = now,
        };
        db.Add(new DocumentContent { Document = document, Bytes = bytes });
        await db.SaveChangesAsync(ct);
        return document;
    }
}

internal static class CoverLetterValidation
{
    public const int MaxBriefLength = 2000;
    private const int MaxLineLength = 200;
    private const int MaxAddressLength = 300;
    private const int MaxParagraphs = 10;
    private const int MaxParagraphLength = 2000;

    // Trims everything, drops blank paragraphs, and rejects what wouldn't fit on a one-page letter.
    public static CoverLetterContent Normalize(CoverLetterContentInput input, Language? postingLanguage)
    {
        var paragraphs = (input.Paragraphs ?? [])
            .Select(p => Text(p, "paragraphs", MaxParagraphLength)).OfType<string>().ToList();
        if (paragraphs.Count > MaxParagraphs)
            throw new InvalidCoverLetterException($"paragraphs: a letter can have at most {MaxParagraphs} paragraphs.");
        return new(
            input.Language ?? postingLanguage ?? Language.English,
            Text(input.FullName, "fullName", MaxLineLength),
            Lines(input.Address, "address"),
            Text(input.Phone, "phone", MaxLineLength),
            Text(input.Email, "email", MaxLineLength),
            input.Date,
            Text(input.Organization, "organization", MaxLineLength),
            Text(input.RecipientName, "recipientName", MaxLineLength),
            Lines(input.RecipientAddress, "recipientAddress"),
            Text(input.Subject, "subject", MaxLineLength),
            Text(input.Greeting, "greeting", MaxLineLength),
            paragraphs,
            Text(input.Closing, "closing", MaxLineLength));
    }

    public static CoverLetterContentInput ToInput(CoverLetterContent c) => new(c.Language, c.FullName, c.Address,
        c.Phone, c.Email, c.Date, c.Organization, c.RecipientName, c.RecipientAddress, c.Subject, c.Greeting,
        c.Paragraphs, c.Closing);

    public static string? Text(string? value, string field, int maxLength)
    {
        var text = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (text?.Length > maxLength)
            throw new InvalidCoverLetterException($"{field}: at most {maxLength} characters.");
        return text;
    }

    private static string? Lines(string? value, string field) =>
        Text(string.Join("\n", AddressLines(value)), field, MaxAddressLength);

    // Postal addresses print street and house number, then postcode and town, each on its own line.
    // A one-line address like the CV's "Storgata 1, 0155 Oslo" is split at its commas.
    public static List<string> AddressLines(string? value)
    {
        var lines = (value ?? "").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        return lines.Count == 1
            ? [.. lines[0].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)]
            : lines;
    }
}

[ObjectType<CoverLetterDraft>]
public static partial class CoverLetterDraftNode
{
    static partial void Configure(IObjectTypeDescriptor<CoverLetterDraft> descriptor)
    {
        descriptor.Ignore(d => d.UserId);
        descriptor.Ignore(d => d.ListingId);
        descriptor.Ignore(d => d.Listing);
        descriptor.Ignore(d => d.ContentJson);
    }
}
