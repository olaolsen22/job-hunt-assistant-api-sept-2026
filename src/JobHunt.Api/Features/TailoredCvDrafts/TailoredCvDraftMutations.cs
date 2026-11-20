using JobHunt.Api.Data;
using JobHunt.Api.Features.Documents;
using JobHunt.Api.Features.Listings;
using JobHunt.Api.Features.Profiles;
using Microsoft.EntityFrameworkCore;

namespace JobHunt.Api.Features.TailoredCvDrafts;

public record TailoredCvBulletInput(string Text, int? SourceBulletId = null);
public record TailoredCvExperienceInput(string Employer, string Title, DateOnly? StartMonth, DateOnly? EndMonth,
    List<TailoredCvBulletInput> Bullets, int? SourceEntryId = null, string? Location = null,
    string? Paragraph = null, List<string>? Technologies = null);
public record TailoredCvEducationInput(string School, string? Degree, string? Field, DateOnly? StartMonth,
    DateOnly? EndMonth, List<TailoredCvBulletInput> Bullets, int? SourceEntryId = null, string? Location = null,
    string? Paragraph = null);
public record TailoredCvCertificationInput(string Name, string? Issuer, DateOnly? IssuedMonth, DateOnly? ExpiresMonth,
    string? Url, List<TailoredCvBulletInput> Bullets, int? SourceEntryId = null,
    string? Paragraph = null);
public record TailoredCvProjectInput(string Name, string? Url, DateOnly? StartMonth, DateOnly? EndMonth,
    List<TailoredCvBulletInput> Bullets, int? SourceEntryId = null, string? Location = null,
    string? Paragraph = null, ProjectKind? Kind = null, List<string>? Technologies = null);
public record TailoredCvSkillInput(string Name, int? Level, int? SourceSkillId = null, int? CategoryId = null,
    string? Group = null);
public record TailoredCvLanguageInput(string Name, LanguageProficiency? Written, LanguageProficiency? Spoken);
// A null Language means the listing's posting language, or English when the listing has none.
public record TailoredCvContentInput(string? Summary = null,
    List<TailoredCvExperienceInput>? Experiences = null,
    List<TailoredCvEducationInput>? Educations = null,
    List<TailoredCvCertificationInput>? Certifications = null,
    List<TailoredCvProjectInput>? Projects = null,
    List<TailoredCvSkillInput>? Skills = null,
    List<TailoredCvLanguageInput>? Languages = null,
    string? FullName = null,
    string? Headline = null,
    string? Email = null,
    string? Phone = null,
    string? Address = null,
    List<TailoredCvLinkInput>? Links = null,
    Language? Language = null,
    DateOnly? DateOfBirth = null,
    bool ReferencesOnRequest = false,
    TailoredCvSectionOrder? SectionOrder = null,
    TailoredCvContentStyle? ContentStyle = null,
    TailoredCvTemplate? Template = null);
public record TailoredCvLinkInput(string Label, string Url);

public class InvalidTailoredCvDraftException(string message) : Exception(message);

public class TailoredCvDraftNotFoundException(int listingId)
    : Exception($"No tailored CV draft was found for listing {listingId}.");

[QueryType]
public static class TailoredCvDraftQueries
{
    public static Task<TailoredCvDraft?> GetTailoredCvDraftAsync(int listingId, AppDbContext db, CancellationToken ct) =>
        db.TailoredCvDrafts.AsNoTracking().FirstOrDefaultAsync(d => d.ListingId == listingId, ct);
}

[MutationType]
public static class TailoredCvDraftMutations
{
    [Error<ListingNotFoundException>]
    [Error<InvalidTailoredCvDraftException>]
    public static async Task<TailoredCvDraft> SaveTailoredCvDraftAsync(
        int listingId, TailoredCvContentInput content, AppDbContext db, TimeProvider time, CancellationToken ct)
    {
        var listing = await db.Listings.FirstOrDefaultAsync(l => l.Id == listingId, ct)
            ?? throw new ListingNotFoundException(listingId);
        var normalized = TailoredCvDraftValidation.Normalize(content, listing.PostingLanguage);
        await TailoredCvDraftValidation.ValidateSourceIdsAsync(normalized, db, ct);
        normalized = await TailoredCvDraftValidation.FillMissingLocationsAsync(normalized, db, ct);

        var draft = await db.TailoredCvDrafts.FirstOrDefaultAsync(d => d.ListingId == listingId, ct);
        var now = time.GetUtcNow().UtcDateTime;
        if (draft is null)
        {
            draft = new TailoredCvDraft { UserId = db.CurrentUserId!, ListingId = listingId, CreatedAt = now };
            db.TailoredCvDrafts.Add(draft);
        }
        draft.Content = normalized;
        draft.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        return draft;
    }

    // Each generation preserves existing documents; remove old ones with deleteDocument.
    [Error<ListingNotFoundException>]
    [Error<TailoredCvDraftNotFoundException>]
    public static async Task<Document> GenerateCvDocumentAsync(
        int listingId, AppDbContext db, TimeProvider time, CancellationToken ct)
    {
        var names = await DocumentMutations.ListingNamesAsync(listingId, db, ct);
        var draft = await db.TailoredCvDrafts.AsNoTracking().FirstOrDefaultAsync(d => d.ListingId == listingId, ct)
            ?? throw new TailoredCvDraftNotFoundException(listingId);

        var listing = await db.Listings.AsNoTracking().Where(l => l.Id == listingId)
            .Select(l => new { l.WorkMode, Cities = l.Locations.Select(x => x.Location.City).ToList() })
            .SingleAsync(ct);
        var relocateTo = TailoredCvPdfRenderer.RelocationTarget(draft.Content.Address, listing.WorkMode, listing.Cities);
        var bytes = await RenderPdfAsync(draft.Content, db, ct, relocateTo);
        var document = DocumentMutations.AddFile(db, listingId, names, DocumentKind.Cv, DocumentOrigin.Tailored, bytes,
            time.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync(ct);
        return document;
    }

    // With the user's skill categories, which the developer layout groups skills by.
    internal static async Task<byte[]> RenderPdfAsync(TailoredCvContent content, AppDbContext db, CancellationToken ct,
        string? relocateTo = null)
    {
        var categories = await db.SkillCategories.AsNoTracking().Include(c => c.Translations)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Id).ToListAsync(ct);
        return TailoredCvPdfRenderer.Render(content, categories, relocateTo);
    }
}

[ObjectType<TailoredCvDraft>]
public static partial class TailoredCvDraftNode
{
    static partial void Configure(IObjectTypeDescriptor<TailoredCvDraft> descriptor)
    {
        descriptor.Ignore(d => d.UserId);
        descriptor.Ignore(d => d.ListingId);
        descriptor.Ignore(d => d.Listing);
        descriptor.Ignore(d => d.ContentJson);
    }
}
