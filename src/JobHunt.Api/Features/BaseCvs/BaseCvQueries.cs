using JobHunt.Api.Data;
using JobHunt.Api.Features.Listings;
using JobHunt.Api.Features.Profiles;
using JobHunt.Api.Features.TailoredCvDrafts;
using Microsoft.EntityFrameworkCore;

namespace JobHunt.Api.Features.BaseCvs;

[QueryType]
public static class BaseCvQueries
{
    // A handful per user, so a plain list by name with every version instead of paging.
    public static Task<List<BaseCv>> GetBaseCvsAsync(AppDbContext db, CancellationToken ct, bool archived = false) =>
        db.BaseCvs.AsNoTracking()
            .Include(b => b.Versions)
            .Where(b => (b.ArchivedAt != null) == archived)
            .OrderBy(b => b.NormalizedName)
            .ToListAsync(ct);

    public static Task<BaseCvVersion?> GetBaseCvVersionAsync(int id, AppDbContext db, CancellationToken ct) =>
        db.BaseCvVersions.AsNoTracking().Include(v => v.BaseCv).FirstOrDefaultAsync(v => v.Id == id, ct);

    // Starting point for a new base CV or version: the whole profile in the requested language. Nothing is saved.
    public static async Task<TailoredCvContent> GetBaseCvFromProfileAsync(
        Language language, AppDbContext db, CancellationToken ct) =>
        TailoredCvDraftFromProfileQueries.FromProfile(await ProfileQueries.LoadAsync(db, ct), language);

    // Starting point for the base CV in another language: its latest version in fromLanguage (default: the newest
    // version in any other language) with profile text swapped for the profile's translations. Nothing is saved.
    // Null when the base CV isn't the user's or has no version to translate from.
    public static async Task<BaseCvTranslation?> GetBaseCvTranslationAsync(int baseCvId, Language language,
        AppDbContext db, CancellationToken ct, Language? fromLanguage = null)
    {
        var source = await db.BaseCvVersions.AsNoTracking()
            .Where(v => v.BaseCvId == baseCvId && (fromLanguage == null ? v.Language != language : v.Language == fromLanguage))
            .OrderByDescending(v => v.Id)
            .FirstOrDefaultAsync(ct);
        return source is null
            ? null
            : BaseCvTranslator.Translate(source.Content, source.Language, language, await ProfileQueries.LoadAsync(db, ct));
    }
}

internal static class BaseCvDataLoaders
{
    // With its base CV, so a document can show "Frontend (EN) v3".
    [DataLoader]
    public static async Task<Dictionary<int, BaseCvVersion>> GetBaseCvVersionByIdAsync(
        IReadOnlyList<int> ids, AppDbContext db, CancellationToken ct) =>
        await db.BaseCvVersions.AsNoTracking().Include(v => v.BaseCv)
            .Where(v => ids.Contains(v.Id))
            .ToDictionaryAsync(v => v.Id, ct);
}
