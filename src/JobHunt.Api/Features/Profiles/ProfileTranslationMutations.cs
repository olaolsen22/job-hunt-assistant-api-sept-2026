using JobHunt.Api.Data;
using JobHunt.Api.Features.Listings;
using Microsoft.EntityFrameworkCore;

namespace JobHunt.Api.Features.Profiles;

[MutationType]
public static class ProfileTranslationMutations
{
    private const int MaxBulletLength = 500;

    // A separate mutation from updateProfile, which is a full replace of the basics.
    [Error<InvalidProfileException>]
    public static async Task<Profile> SetProfileContentLanguageAsync(Language language, AppDbContext db, CancellationToken ct)
    {
        var profile = await db.Profiles.SingleAsync(ct);
        // Relabelling the text would make every translation's source wrong, so the user deletes them first.
        if (profile.ContentLanguage != language
            && (await db.ProfileBulletTranslations.AnyAsync(ct) || await db.ProfileTranslations.AnyAsync(ct)))
            throw new InvalidProfileException("Delete all translations before changing the content language.");
        profile.ContentLanguage = language;
        await db.SaveChangesAsync(ct);
        return await ProfileQueries.LoadAsync(db, ct);
    }

    // Upsert per (bullet, language). isMachine is for a later auto-translate; setting by hand clears it.
    [Error<ProfileEntryNotFoundException>]
    [Error<InvalidProfileException>]
    public static async Task<ProfileBulletTranslation> SetProfileBulletTranslationAsync(
        int bulletId, Language language, string text, AppDbContext db, CancellationToken ct, bool isMachine = false)
    {
        var bullet = await db.ProfileBullets.FirstOrDefaultAsync(b => b.Id == bulletId, ct)
            ?? throw new ProfileEntryNotFoundException("Bullet", bulletId);
        await CheckLanguageAsync(language, db, ct);
        var cleanText = ProfileMutations.Required(text, "Translation", MaxBulletLength);

        var translation = await db.ProfileBulletTranslations
            .FirstOrDefaultAsync(t => t.BulletId == bulletId && t.Language == language, ct);
        if (translation is null)
            db.ProfileBulletTranslations.Add(translation = new ProfileBulletTranslation
            {
                UserId = bullet.UserId, Bullet = bullet, Language = language,
            });
        translation.Bullet = bullet;
        translation.Text = cleanText;
        translation.SourceText = bullet.Text;
        translation.IsMachine = isMachine;
        await db.SaveChangesAsync(ct);
        return translation;
    }

    [Error<ProfileEntryNotFoundException>]
    [UseMutationConvention(PayloadFieldName = "id")]
    public static async Task<int> DeleteProfileBulletTranslationAsync(int id, AppDbContext db, CancellationToken ct)
    {
        if (await db.ProfileBulletTranslations.Where(t => t.Id == id).ExecuteDeleteAsync(ct) == 0)
            throw new ProfileEntryNotFoundException("Translation", id);
        return id;
    }

    // Full replace of the language's headline and summary: a field left out is cleared.
    [Error<InvalidProfileException>]
    public static async Task<ProfileTranslation> SetProfileTranslationAsync(
        Language language, string? headline, string? summary, AppDbContext db, CancellationToken ct, bool isMachine = false)
    {
        var cleanHeadline = ProfileMutations.Text(headline, "Headline");
        var cleanSummary = ProfileMutations.Markdown(summary, "Summary");
        await CheckLanguageAsync(language, db, ct);
        if (cleanHeadline is null && cleanSummary is null)
            throw new InvalidProfileException("Give a translated headline or summary.");

        var profile = await db.Profiles.Include(p => p.Translations).SingleAsync(ct);
        if (cleanHeadline is not null && profile.Headline is null)
            throw new InvalidProfileException("The profile has no headline to translate.");
        if (cleanSummary is not null && profile.Summary is null)
            throw new InvalidProfileException("The profile has no summary to translate.");

        var translation = profile.Translations.FirstOrDefault(t => t.Language == language);
        if (translation is null)
            profile.Translations.Add(translation = new ProfileTranslation { UserId = profile.UserId, Language = language });
        translation.Headline = cleanHeadline;
        translation.Summary = cleanSummary;
        translation.SourceHeadline = profile.Headline;
        translation.SourceSummary = profile.Summary;
        translation.IsMachine = isMachine;
        await db.SaveChangesAsync(ct);
        return translation;
    }

    [Error<ProfileEntryNotFoundException>]
    [UseMutationConvention(PayloadFieldName = "language")]
    public static async Task<Language> DeleteProfileTranslationAsync(Language language, AppDbContext db, CancellationToken ct)
    {
        if (await db.ProfileTranslations.Where(t => t.Language == language).ExecuteDeleteAsync(ct) == 0)
            throw new ProfileEntryNotFoundException("Translation", language);
        return language;
    }

    internal static async Task CheckLanguageAsync(Language language, AppDbContext db, CancellationToken ct)
    {
        if (language == (await db.Profiles.SingleAsync(ct)).ContentLanguage)
            throw new InvalidProfileException("A translation can't be in the language the profile is written in.");
    }
}
