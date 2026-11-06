using JobHunt.Api.Data;
using JobHunt.Api.Features.Listings;
using JobHunt.Api.Features.Profiles;
using JobHunt.Api.Features.Skills;
using Microsoft.EntityFrameworkCore;

namespace JobHunt.Api.Features.TailoredCvDrafts;

// Shared by both AI paths so they write in the language the draft will be rendered in.
public static class TailoredCvLanguageText
{
    // A language the UI asks for wins (switching regenerates the one draft); else a saved draft keeps its language,
    // else the listing's posting language decides.
    public static async Task<Language> TargetLanguageAsync(Listing listing, Language? requested, AppDbContext db,
        CancellationToken ct)
    {
        if (requested is { } language) return language;
        var draft = await db.TailoredCvDrafts.AsNoTracking().FirstOrDefaultAsync(d => d.ListingId == listing.Id, ct);
        return draft?.Content.Language ?? listing.PostingLanguage ?? Language.English;
    }

    // The translation in the target language when one exists, else the original.
    public static string Bullet(ProfileBullet bullet, Language language) =>
        bullet.Translations.FirstOrDefault(t => t.Language == language)?.Text ?? bullet.Text;

    // An entry's tagged skills in the draft language, the candidate's stronger (higher level) skills first.
    public static List<string> Technologies(IEnumerable<Skill> skills, Profile profile, Language language)
    {
        var levels = profile.Skills.ToDictionary(s => s.SkillId, s => s.Level);
        return [.. skills.OrderByDescending(s => levels.GetValueOrDefault(s.Id)).ThenBy(s => s.Name)
            .Select(s => s.NameIn(language))];
    }

    public static string Name(Language language) => language == Language.Norwegian ? "Norwegian (bokmål)" : "American English (US spelling: color, optimize, internationalization)";
}
