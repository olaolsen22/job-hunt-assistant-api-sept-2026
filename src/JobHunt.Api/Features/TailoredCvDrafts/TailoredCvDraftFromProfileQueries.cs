using JobHunt.Api.Data;
using JobHunt.Api.Features.Listings;
using JobHunt.Api.Features.Profiles;
using Microsoft.EntityFrameworkCore;

namespace JobHunt.Api.Features.TailoredCvDrafts;

[QueryType]
public static class TailoredCvDraftFromProfileQueries
{
    // The whole profile as unsaved draft content. Language: requested, else saved draft's, else listing's. Null if unknown.
    public static async Task<TailoredCvContent?> GetTailoredCvDraftFromProfileAsync(
        int listingId, AppDbContext db, CancellationToken ct, Language? language = null)
    {
        var listing = await db.Listings.AsNoTracking().FirstOrDefaultAsync(l => l.Id == listingId, ct);
        if (listing is null) return null;
        var profile = await ProfileQueries.LoadAsync(db, ct);
        return FromProfile(profile, await TailoredCvLanguageText.TargetLanguageAsync(listing, language, db, ct));
    }

    // The whole profile as CV content in the target language, with source ids; shared with base CVs.
    internal static TailoredCvContent FromProfile(Profile profile, Language target)
    {
        var translation = profile.Translations.FirstOrDefault(t => t.Language == target);

        List<TailoredCvBullet> Bullets(IEnumerable<ProfileBullet> bullets) =>
            [.. bullets.Select(b => new TailoredCvBullet(TailoredCvLanguageText.Bullet(b, target), b.Id))];
        string? Place(Locations.Location? location) =>
            location is null ? null : TailoredCvDraftValidation.FormatPlace(location, target);

        return new TailoredCvContent(
            Summary: translation?.Summary ?? profile.Summary,
            Experiences: [.. profile.Experiences.Select(e => new TailoredCvExperience(
                e.Employer, e.TitleIn(target), e.StartMonth, e.EndMonth, Bullets(e.Bullets), e.Id, Place(e.Location),
                Technologies: TailoredCvLanguageText.Technologies(e.Skills, profile, target)))],
            Educations: [.. profile.Educations.Select(e => new TailoredCvEducation(
                e.School, e.DegreeIn(target), e.FieldIn(target), e.StartMonth, e.EndMonth, Bullets(e.Bullets), e.Id, Place(e.Location)))],
            Certifications: [.. profile.Certifications.Select(c => new TailoredCvCertification(
                c.Name, c.Issuer, c.IssuedMonth, c.ExpiresMonth, c.Url, Bullets(c.Bullets), c.Id))],
            Projects: [.. profile.Projects.Select(p => new TailoredCvProject(
                p.Name, p.Url, p.StartMonth, p.EndMonth, Bullets(p.Bullets), p.Id, Place(p.Location), Kind: p.Kind,
                Technologies: TailoredCvLanguageText.Technologies(p.Skills, profile, target)))],
            Skills: [.. profile.Skills.Select(s => new TailoredCvSkill(s.Skill.NameIn(target), s.Level, s.SkillId, s.Skill.CategoryId))],
            Languages: [.. profile.Languages.Select(l => new TailoredCvLanguage(l.Name, l.Written, l.Spoken))],
            FullName: profile.FullName,
            Headline: translation?.Headline ?? profile.Headline,
            Email: profile.Email,
            Phone: profile.Phone,
            Address: Place(profile.Location),
            Links: [.. profile.Links.Select(l => new TailoredCvLink(LinkLabel(l), l.Url))],
            Language: target,
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
