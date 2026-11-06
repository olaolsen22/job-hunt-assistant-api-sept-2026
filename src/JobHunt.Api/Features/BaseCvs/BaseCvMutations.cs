using System.Text.Json;
using JobHunt.Api.Data;
using JobHunt.Api.Features.Documents;
using JobHunt.Api.Features.Listings;
using JobHunt.Api.Features.TailoredCvDrafts;
using Microsoft.EntityFrameworkCore;

namespace JobHunt.Api.Features.BaseCvs;

public class InvalidBaseCvException(string message) : Exception(message);
public class BaseCvNotFoundException(int id) : Exception($"Base CV {id} was not found.");
public class BaseCvVersionNotFoundException(int id) : Exception($"Base CV version {id} was not found.");
public class BaseCvInUseException(int id)
    : Exception($"Base CV {id} has versions on application documents and can't be deleted. Archive it instead.");
public class DuplicateBaseCvException(int existingBaseCvId) : Exception("A base CV with this name already exists.")
{
    public int ExistingBaseCvId { get; } = existingBaseCvId;
}

[MutationType]
public static class BaseCvMutations
{
    internal const int MaxNameLength = 100;
    private const int MaxDescriptionLength = 500;
    private const int MaxNoteLength = 500;

    [Error<InvalidBaseCvException>]
    [Error<DuplicateBaseCvException>]
    [Error<InvalidTailoredCvDraftException>]
    public static async Task<BaseCv> CreateBaseCvAsync(string name, string? description, Language language,
        TailoredCvContentInput content, string? note, AppDbContext db, TimeProvider time, CancellationToken ct)
    {
        var (cleanName, normalizedName) = await NameAsync(name, null, db, ct);
        var now = time.GetUtcNow().UtcDateTime;
        var baseCv = new BaseCv
        {
            UserId = db.CurrentUserId!,
            Name = cleanName,
            NormalizedName = normalizedName,
            Description = Text(description, "description", MaxDescriptionLength),
            CreatedAt = now,
            UpdatedAt = now,
        };
        baseCv.Versions.Add(NewVersion(baseCv, language, 1, await ContentAsync(content, language, db, ct),
            Text(note, "note", MaxNoteLength), now, db));
        db.BaseCvs.Add(baseCv);
        await db.SaveChangesAsync(ct);
        return baseCv;
    }

    // Numbered per language; a language the base CV doesn't have yet starts at 1. Unchanged content returns the latest
    // version in that language and ignores the note: a new number only when the CV changed, so outcomes aren't split
    // across identical versions.
    [Error<BaseCvNotFoundException>]
    [Error<InvalidBaseCvException>]
    [Error<InvalidTailoredCvDraftException>]
    public static async Task<BaseCvVersion> SaveBaseCvVersionAsync(int baseCvId, Language language,
        TailoredCvContentInput content, string? note, AppDbContext db, TimeProvider time, CancellationToken ct)
    {
        var baseCv = await db.BaseCvs.FirstOrDefaultAsync(b => b.Id == baseCvId, ct)
            ?? throw new BaseCvNotFoundException(baseCvId);
        var cleanNote = Text(note, "note", MaxNoteLength);
        var normalized = await ContentAsync(content, language, db, ct);
        var latest = await db.BaseCvVersions.Where(v => v.BaseCvId == baseCvId && v.Language == language)
            .OrderByDescending(v => v.Number).FirstOrDefaultAsync(ct);
        if (latest is not null && JsonSerializer.Serialize(normalized, TailoredCvJson.Options) == latest.ContentJson)
            return latest;

        var now = time.GetUtcNow().UtcDateTime;
        var version = NewVersion(baseCv, language, (latest?.Number ?? 0) + 1, normalized, cleanNote, now, db);
        db.BaseCvVersions.Add(version);
        baseCv.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        return version;
    }

    [Error<BaseCvNotFoundException>]
    [Error<InvalidBaseCvException>]
    [Error<DuplicateBaseCvException>]
    public static async Task<BaseCv> UpdateBaseCvAsync(int id, string name, string? description, AppDbContext db,
        TimeProvider time, CancellationToken ct)
    {
        var baseCv = await LoadAsync(id, db, ct);
        (baseCv.Name, baseCv.NormalizedName) = await NameAsync(name, id, db, ct);
        baseCv.Description = Text(description, "description", MaxDescriptionLength);
        baseCv.UpdatedAt = time.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        return baseCv;
    }

    // Keeps the first ArchivedAt, like listings.
    [Error<BaseCvNotFoundException>]
    public static async Task<BaseCv> SetBaseCvArchivedAsync(int id, bool isArchived, AppDbContext db,
        TimeProvider time, CancellationToken ct)
    {
        var baseCv = await LoadAsync(id, db, ct);
        if (isArchived)
            baseCv.ArchivedAt ??= time.GetUtcNow().UtcDateTime;
        else
            baseCv.ArchivedAt = null;
        await db.SaveChangesAsync(ct);
        return baseCv;
    }

    // A version on a document is the record of what an application sent, so only an unused base CV is deleted.
    [Error<BaseCvNotFoundException>]
    [Error<BaseCvInUseException>]
    [UseMutationConvention(PayloadFieldName = "id")]
    public static async Task<int> DeleteBaseCvAsync(int id, AppDbContext db, CancellationToken ct)
    {
        if (!await db.BaseCvs.AnyAsync(b => b.Id == id, ct))
            throw new BaseCvNotFoundException(id);
        if (await db.BaseCvVersions.AnyAsync(v => v.BaseCvId == id && db.Documents.Any(d => d.BaseCvVersionId == v.Id), ct))
            throw new BaseCvInUseException(id);
        // The database cascades the versions.
        await db.BaseCvs.Where(b => b.Id == id).ExecuteDeleteAsync(ct);
        return id;
    }

    // The base CV sent as-is: a new unsent CV on the listing, sent like any other by applyToListing.
    [Error<ListingNotFoundException>]
    [Error<BaseCvVersionNotFoundException>]
    public static async Task<Document> GenerateBaseCvDocumentAsync(int listingId, int baseCvVersionId,
        AppDbContext db, TimeProvider time, CancellationToken ct)
    {
        var listing = await db.Listings
            .Where(l => l.Id == listingId)
            .Select(l => new { Company = l.Company.Name, JobRole = l.JobRole.Name })
            .FirstOrDefaultAsync(ct)
            ?? throw new ListingNotFoundException(listingId);
        var version = await db.BaseCvVersions.AsNoTracking().FirstOrDefaultAsync(v => v.Id == baseCvVersionId, ct)
            ?? throw new BaseCvVersionNotFoundException(baseCvVersionId);

        var bytes = await TailoredCvDraftMutations.RenderPdfAsync(version.Content, db, ct);
        var document = DocumentMutations.AddGenerated(db, listingId, listing.Company, listing.JobRole,
            DocumentKind.Cv, DocumentOrigin.BaseCv, bytes, time.GetUtcNow().UtcDateTime, version.Id);
        await db.SaveChangesAsync(ct);
        return document;
    }

    private static async Task<BaseCv> LoadAsync(int id, AppDbContext db, CancellationToken ct) =>
        await db.BaseCvs.Include(b => b.Versions)
            .FirstOrDefaultAsync(b => b.Id == id, ct)
        ?? throw new BaseCvNotFoundException(id);

    // The same rules as a tailored draft; the content's language is always the version's.
    private static async Task<TailoredCvContent> ContentAsync(TailoredCvContentInput input, Language language,
        AppDbContext db, CancellationToken ct)
    {
        if (input is null) throw new InvalidTailoredCvDraftException("content is required.");
        var content = TailoredCvDraftValidation.Normalize(input with { Language = language }, language);
        await TailoredCvDraftValidation.ValidateSourceIdsAsync(content, db, ct);
        return await TailoredCvDraftValidation.FillMissingLocationsAsync(content, db, ct);
    }

    private static BaseCvVersion NewVersion(BaseCv baseCv, Language language, int number, TailoredCvContent content,
        string? note, DateTime now, AppDbContext db) => new()
    {
        UserId = db.CurrentUserId!,
        BaseCv = baseCv,
        Language = language,
        Number = number,
        Content = content,
        Note = note,
        CreatedAt = now,
    };

    private static async Task<(string Name, string NormalizedName)> NameAsync(string name, int? id, AppDbContext db,
        CancellationToken ct)
    {
        var clean = Inputs.Clean(name);
        if (clean is null || clean.Length > MaxNameLength)
            throw new InvalidBaseCvException($"name: must contain 1 to {MaxNameLength} characters.");
        var normalized = Names.Normalize(clean);
        var existingId = await db.BaseCvs.Where(b => b.Id != id && b.NormalizedName == normalized)
            .Select(b => (int?)b.Id).FirstOrDefaultAsync(ct);
        if (existingId is { } duplicate) throw new DuplicateBaseCvException(duplicate);
        return (clean, normalized);
    }

    private static string? Text(string? value, string field, int maxLength)
    {
        var clean = Inputs.Clean(value);
        if (clean?.Length > maxLength)
            throw new InvalidBaseCvException($"{field}: must be at most {maxLength} characters.");
        return clean;
    }
}
