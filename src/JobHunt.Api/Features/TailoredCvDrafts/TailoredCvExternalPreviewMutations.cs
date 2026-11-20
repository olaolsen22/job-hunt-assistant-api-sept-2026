using System.Text.Json;
using System.Text.Json.Serialization;
using JobHunt.Api.Data;
using JobHunt.Api.Features.Listings;
using JobHunt.Api.Features.Profiles;
using Microsoft.EntityFrameworkCore;

namespace JobHunt.Api.Features.TailoredCvDrafts;

public class TailoredCvExternalCandidateInput
{
    [JsonRequired]
    public int SchemaVersion { get; init; }
    [JsonRequired]
    public TailoredCvContentInput? Content { get; init; }
}

public record TailoredCvExternalCandidate(int SchemaVersion, TailoredCvContent Content);

[MutationType]
public static class TailoredCvExternalPreviewMutations
{
    [Error<ListingNotFoundException>]
    [Error<InvalidTailoredCvDraftException>]
    [UseMutationConvention(PayloadFieldName = "candidate")]
    public static async Task<TailoredCvExternalCandidate> PreviewTailoredCvDraftAsync(
        int listingId, string json, AppDbContext db, CancellationToken ct,
        TailoredCvTemplate? template = null, Language? language = null)
    {
        var listing = await db.Listings.AsNoTracking().Include(l => l.Skills).ThenInclude(s => s.Skill)
            .FirstOrDefaultAsync(l => l.Id == listingId, ct)
            ?? throw new ListingNotFoundException(listingId);
        var skills = listing.Skills.Select(s => s.Skill.Name).ToList();
        var parsed = ParseCandidate(json);
        // The language the prompt was built for wins over whatever the pasted reply claims.
        var input = parsed.Content! with { Language = language ?? parsed.Content!.Language };
        var templateValue = template ?? TailoredCvTemplate.Detailed;
        var mode = TailoredCvWording.ModeFor(templateValue, listing: true);
        var content = TailoredCvDraftValidation.Normalize(input, listing.PostingLanguage) with { Template = templateValue };
        await TailoredCvDraftValidation.ValidateSourceIdsAsync(content, db, ct,
            requireSourceBulletText: mode == TailoredCvWordingMode.Exact);
        if (mode != TailoredCvWordingMode.Exact)
            content = await KeepSourceWordingAsync(content, mode,
                TailoredCvWording.ListingText(listing.Title, listing.DescriptionIn(content.Language), skills), skills, db, ct);
        content = await TailoredCvDraftValidation.FillMissingLocationsAsync(content, db, ct);
        content = await TailoredCvDraftValidation.FillMissingSourceDetailsAsync(content, db, ct);
        return new(parsed.SchemaVersion, content);
    }

    // The pasted reply's shape and limits, shared with the base CV preview.
    internal static TailoredCvExternalCandidateInput ParseCandidate(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > TailoredCvDraftValidation.MaxJsonLength)
            throw new InvalidTailoredCvDraftException(
                $"json: paste a candidate object no longer than {TailoredCvDraftValidation.MaxJsonLength} characters.");

        TailoredCvExternalCandidateInput parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<TailoredCvExternalCandidateInput>(json, TailoredCvJson.Options)
                ?? throw new JsonException();
        }
        catch (JsonException)
        {
            throw new InvalidTailoredCvDraftException("json: invalid candidate JSON or value type.");
        }
        if (parsed.SchemaVersion != 1 || parsed.Content is null)
            throw new InvalidTailoredCvDraftException("schemaVersion/content: expected version 1 and a content object.");

        TailoredCvDraftValidation.ValidateExternalCandidateItems(parsed.Content);
        return parsed;
    }

    // A DETAILED bullet is checked against its own source text and its translation in the draft language. A CONCISE
    // bullet may merge bullets of its entry, so it is held to the whole entry's facts: no number, listing or profile
    // skill those bullets do not state. A paragraph is an overview of the role in plain words, held to the same facts.
    private static async Task<TailoredCvContent> KeepSourceWordingAsync(TailoredCvContent content,
        TailoredCvWordingMode mode, string listingText, List<string> listingSkills, AppDbContext db, CancellationToken ct)
    {
        // Ids were already checked as the user's own by ValidateSourceIdsAsync.
        var profile = await ProfileQueries.LoadAsync(db, ct);
        var sources = profile.BulletLists()
            .SelectMany(entry => entry.Select(bullet => (Bullet: bullet, Entry: entry)))
            .ToDictionary(s => s.Bullet.Id);
        IEnumerable<string> Texts(ProfileBullet bullet) => [bullet.Text, TailoredCvLanguageText.Bullet(bullet, content.Language)];
        var rewrite = mode == TailoredCvWordingMode.Rewrite;
        List<string> skillNames = rewrite
            ? [.. profile.Skills.Select(s => s.Skill).Concat(profile.Experiences.SelectMany(e => e.Skills))
                .Concat(profile.Projects.SelectMany(p => p.Skills))
                .SelectMany(s => new[] { s.Name, s.NameIn(content.Language) }).Concat(listingSkills).Distinct()]
            : [];
        return TailoredCvWording.KeepSourceWording(content, mode,
            id => rewrite ? sources[id].Entry.SelectMany(Texts) : Texts(sources[id].Bullet),
            id => TailoredCvLanguageText.Bullet(sources[id].Bullet, content.Language), listingText, skillNames,
            (paragraph, ids) => TailoredCvWording.KeepsSourceFacts(paragraph,
                ids.SelectMany(id => sources[id].Entry).Distinct().SelectMany(Texts), listingSkills));
    }
}
