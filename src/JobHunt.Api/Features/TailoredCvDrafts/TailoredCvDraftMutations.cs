using JobHunt.Api.Data;
using JobHunt.Api.Features.Listings;
using Microsoft.EntityFrameworkCore;

namespace JobHunt.Api.Features.TailoredCvDrafts;

public record TailoredCvBulletInput(string Text, int? SourceBulletId = null);
public record TailoredCvExperienceInput(string Employer, string Title, DateOnly? StartMonth, DateOnly? EndMonth,
    List<TailoredCvBulletInput> Bullets, int? SourceEntryId = null);
public record TailoredCvEducationInput(string School, string? Degree, string? Field, DateOnly? StartMonth,
    DateOnly? EndMonth, List<TailoredCvBulletInput> Bullets, int? SourceEntryId = null);
public record TailoredCvCertificationInput(string Name, string? Issuer, DateOnly? IssuedMonth, DateOnly? ExpiresMonth,
    string? Url, List<TailoredCvBulletInput> Bullets, int? SourceEntryId = null);
public record TailoredCvProjectInput(string Name, string? Url, DateOnly? StartMonth, DateOnly? EndMonth,
    List<TailoredCvBulletInput> Bullets, int? SourceEntryId = null);
public record TailoredCvSkillInput(string Name, int? Level, int? SourceSkillId = null);
public record TailoredCvLanguageInput(Language Language, CefrLevel? Level);
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
    string? Location = null,
    List<TailoredCvLinkInput>? Links = null);
public record TailoredCvLinkInput(string Label, string Url);

public class InvalidTailoredCvDraftException(string message) : Exception(message);

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
        var normalized = TailoredCvDraftValidation.Normalize(content);
        await TailoredCvDraftValidation.ValidateSourceIdsAsync(normalized, db, ct);

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
