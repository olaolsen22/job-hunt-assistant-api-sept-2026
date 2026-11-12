using JobHunt.Api.Data;
using JobHunt.Api.Features.Listings;
using JobHunt.Api.Features.Profiles;
using Microsoft.EntityFrameworkCore;

namespace JobHunt.Api.Features.RequirementMaps;

public record RequirementEntryRefInput(RequirementEntryKind Kind, int Id);

public record RequirementNeedInput(string Text, RequirementKind Kind, RequirementImportance Importance,
    RequirementMatch Match, List<int>? BulletIds = null, List<RequirementEntryRefInput>? Entries = null,
    string? Note = null);

public record RequirementMapContentInput(List<RequirementNeedInput> Needs);

public class InvalidRequirementMapException(string message) : Exception(message);

public class RequirementMapNotFoundException(int listingId)
    : Exception($"No requirement map was found for listing {listingId}.");

[QueryType]
public static class RequirementMapQueries
{
    public static Task<RequirementMap?> GetRequirementMapAsync(int listingId, AppDbContext db, CancellationToken ct) =>
        db.RequirementMaps.AsNoTracking().FirstOrDefaultAsync(m => m.ListingId == listingId, ct);
}

[MutationType]
public static class RequirementMapMutations
{
    // Full replace. The listing, its status, documents and drafts are untouched.
    [Error<ListingNotFoundException>]
    [Error<InvalidRequirementMapException>]
    public static async Task<RequirementMap> SaveRequirementMapAsync(int listingId, RequirementMapContentInput content,
        AppDbContext db, TimeProvider time, CancellationToken ct)
    {
        if (!await db.Listings.AnyAsync(l => l.Id == listingId, ct)) throw new ListingNotFoundException(listingId);
        var needs = content.Needs.Select(n => new RequirementNeed(n.Text, n.Kind, n.Importance, n.Match, n.BulletIds,
            n.Entries?.Select(e => new RequirementEntryRef(e.Kind, e.Id)).ToList(), n.Note));
        var normalized = RequirementMapValidation.Normalize(needs, await ProfileQueries.LoadAsync(db, ct), lenient: false);

        var map = await db.RequirementMaps.FirstOrDefaultAsync(m => m.ListingId == listingId, ct);
        var now = time.GetUtcNow().UtcDateTime;
        if (map is null)
        {
            map = new RequirementMap { UserId = db.CurrentUserId!, ListingId = listingId, CreatedAt = now };
            db.RequirementMaps.Add(map);
        }
        map.Content = normalized;
        map.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        return map;
    }

    [Error<RequirementMapNotFoundException>]
    [UseMutationConvention(PayloadFieldName = "listingId")]
    public static async Task<int> DeleteRequirementMapAsync(int listingId, AppDbContext db, CancellationToken ct)
    {
        if (await db.RequirementMaps.Where(m => m.ListingId == listingId).ExecuteDeleteAsync(ct) == 0)
            throw new RequirementMapNotFoundException(listingId);
        return listingId;
    }
}

internal static class RequirementMapValidation
{
    public const int MaxNeeds = 30;
    public const int MaxTextLength = 200;
    public const int MaxNoteLength = 500;
    public const int MaxReferences = 10;

    // A user's save is rejected on any fault. A model's answer (lenient) is corrected instead: one invented id should
    // not cost the whole map, and the result is reviewed before it can be saved. Corrections only understate fit.
    public static RequirementMapContent Normalize(IEnumerable<RequirementNeed> needs, Profile profile, bool lenient)
    {
        var bulletIds = profile.AllBullets().Select(b => b.Id).ToHashSet();
        var entryIds = new Dictionary<RequirementEntryKind, HashSet<int>>
        {
            [RequirementEntryKind.Experience] = [.. profile.Experiences.Select(e => e.Id)],
            [RequirementEntryKind.Education] = [.. profile.Educations.Select(e => e.Id)],
            [RequirementEntryKind.Certification] = [.. profile.Certifications.Select(c => c.Id)],
            [RequirementEntryKind.Project] = [.. profile.Projects.Select(p => p.Id)],
        };

        var result = new List<RequirementNeed>();
        foreach (var need in needs)
        {
            var text = Text(need.Text, "text", MaxTextLength, lenient);
            if (text is null)
            {
                if (lenient) continue;
                throw new InvalidRequirementMapException("text: every need requires a text.");
            }
            var note = Text(need.Note, "note", MaxNoteLength, lenient);
            var bullets = References(need.BulletIds.Distinct(), bulletIds.Contains, "bulletIds", lenient);
            var entries = References(need.Entries.Distinct(), e => entryIds[e.Kind].Contains(e.Id), "entries", lenient);
            var match = need.Match;
            if (match != RequirementMatch.Gap && bullets.Count == 0 && entries.Count == 0 && note is null)
            {
                if (!lenient)
                    throw new InvalidRequirementMapException(
                        $"\"{text}\": an evidenced or partial need requires evidence or a note.");
                match = RequirementMatch.Gap;
            }
            // A gap states that the profile shows nothing, so it can't also point at evidence.
            if (match == RequirementMatch.Gap) (bullets, entries) = ([], []);
            result.Add(new(text, need.Kind, need.Importance, match, bullets, entries, note));
        }

        if (result.Count == 0) throw new InvalidRequirementMapException("needs: the map has no needs.");
        if (result.Count > MaxNeeds)
        {
            if (!lenient) throw new InvalidRequirementMapException($"needs: at most {MaxNeeds} needs.");
            result = result[..MaxNeeds];
        }
        return new(result);
    }

    private static string? Text(string? value, string field, int maxLength, bool lenient)
    {
        var text = Inputs.Clean(value);
        if (text is null || text.Length <= maxLength) return text;
        if (lenient) return text[..maxLength].TrimEnd();
        throw new InvalidRequirementMapException($"{field}: at most {maxLength} characters.");
    }

    private static List<T> References<T>(IEnumerable<T> references, Func<T, bool> exists, string field, bool lenient)
    {
        var all = references.ToList();
        if (lenient) return [.. all.Where(exists).Take(MaxReferences)];
        if (all.Count > MaxReferences)
            throw new InvalidRequirementMapException($"{field}: at most {MaxReferences} references per need.");
        if (!all.All(exists))
            throw new InvalidRequirementMapException($"{field}: a reference is not in your profile.");
        return all;
    }
}
