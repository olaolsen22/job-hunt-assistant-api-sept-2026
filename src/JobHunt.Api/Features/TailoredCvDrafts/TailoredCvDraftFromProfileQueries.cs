using JobHunt.Api.Data;
using JobHunt.Api.Features.Profiles;
using Microsoft.EntityFrameworkCore;

namespace JobHunt.Api.Features.TailoredCvDrafts;

[QueryType]
public static class TailoredCvDraftFromProfileQueries
{
    // Starting point for a reset: the whole profile as draft content. Nothing is saved; the UI loads it into the
    // editor and the user saves explicitly. Keeps the saved draft's language, otherwise the listing's; null for an unknown listing.
    public static async Task<TailoredCvContent?> GetTailoredCvDraftFromProfileAsync(
        int listingId, AppDbContext db, CancellationToken ct)
    {
        var listing = await db.Listings.AsNoTracking().FirstOrDefaultAsync(l => l.Id == listingId, ct);
        if (listing is null) return null;
        var profile = await ProfileQueries.LoadAsync(db, ct);
        var language = await TailoredCvLanguageText.TargetLanguageAsync(listing, db, ct);
        var translation = profile.Translations.FirstOrDefault(t => t.Language == language);

        List<TailoredCvBullet> Bullets(IEnumerable<ProfileBullet> bullets) =>
            [.. bullets.Select(b => new TailoredCvBullet(TailoredCvLanguageText.Bullet(b, language), b.Id))];
        string? Place(Locations.Location? location) =>
            location is null ? null : TailoredCvDraftValidation.FormatPlace(location, language);

        return new TailoredCvContent(
            Summary: translation?.Summary ?? profile.Summary,
            Experiences: [.. profile.Experiences.Select(e => new TailoredCvExperience(
                e.Employer, e.Title, e.StartMonth, e.EndMonth, Bullets(e.Bullets), e.Id, Place(e.Location)))],
            Educations: [.. profile.Educations.Select(e => new TailoredCvEducation(
                e.School, e.Degree, e.Field, e.StartMonth, e.EndMonth, Bullets(e.Bullets), e.Id, Place(e.Location)))],
            Certifications: [.. profile.Certifications.Select(c => new TailoredCvCertification(
                c.Name, c.Issuer, c.IssuedMonth, c.ExpiresMonth, c.Url, Bullets(c.Bullets), c.Id))],
            Projects: [.. profile.Projects.Select(p => new TailoredCvProject(
                p.Name, p.Url, p.StartMonth, p.EndMonth, Bullets(p.Bullets), p.Id, Place(p.Location)))],
            Skills: [.. profile.Skills.Select(s => new TailoredCvSkill(s.Skill.Name, s.Level, s.SkillId))],
            Languages: [.. profile.Languages.Select(l => new TailoredCvLanguage(l.Name, l.Written, l.Spoken))],
            FullName: profile.FullName,
            Headline: translation?.Headline ?? profile.Headline,
            Email: profile.Email,
            Phone: profile.Phone,
            Address: Place(profile.Location),
            Links: [.. profile.Links.Select(l => new TailoredCvLink(LinkLabel(l), l.Url))],
            Language: language,
            DateOfBirth: profile.DateOfBirth);
    }

    private static string LinkLabel(ProfileLink link) => link.Kind switch
    {
        LinkKind.Github => "GitHub",
        LinkKind.Linkedin => "LinkedIn",
        LinkKind.Portfolio => "Portfolio",
        _ => link.Label,
    };
}
