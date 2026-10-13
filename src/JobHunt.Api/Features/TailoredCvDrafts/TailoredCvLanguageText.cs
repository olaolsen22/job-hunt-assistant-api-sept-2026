using JobHunt.Api.Data;
using JobHunt.Api.Features.Listings;
using JobHunt.Api.Features.Profiles;
using Microsoft.EntityFrameworkCore;

namespace JobHunt.Api.Features.TailoredCvDrafts;

// Shared by both AI paths so they write in the language the draft will be rendered in.
public static class TailoredCvLanguageText
{
    // A saved draft keeps its chosen language; otherwise the listing's posting language decides.
    public static async Task<Language> TargetLanguageAsync(Listing listing, AppDbContext db, CancellationToken ct)
    {
        var draft = await db.TailoredCvDrafts.AsNoTracking().FirstOrDefaultAsync(d => d.ListingId == listing.Id, ct);
        return draft?.Content.Language ?? listing.PostingLanguage ?? Language.English;
    }

    // The translation in the target language when one exists, else the original.
    public static string Bullet(ProfileBullet bullet, Language language) =>
        bullet.Translations.FirstOrDefault(t => t.Language == language)?.Text ?? bullet.Text;

    public static string Name(Language language) => language == Language.Norwegian ? "Norwegian (bokmål)" : "English";
}
