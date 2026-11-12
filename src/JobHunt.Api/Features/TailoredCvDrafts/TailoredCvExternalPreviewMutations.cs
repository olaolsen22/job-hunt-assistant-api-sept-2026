using System.Text.Json;
using System.Text.Json.Serialization;
using JobHunt.Api.Data;
using JobHunt.Api.Features.Listings;
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
        var listing = await db.Listings.Where(l => l.Id == listingId)
            .Select(l => new { l.PostingLanguage, l.Title, l.Description, Skills = l.Skills.Select(s => s.Skill.Name).ToList() })
            .FirstOrDefaultAsync(ct)
            ?? throw new ListingNotFoundException(listingId);
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
                TailoredCvWording.ListingText(listing.Title, listing.Description, listing.Skills), db, ct);
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

    // Each bullet is checked against its own source text and its translation in the draft language.
    private static async Task<TailoredCvContent> KeepSourceWordingAsync(TailoredCvContent content,
        TailoredCvWordingMode mode, string listingText, AppDbContext db, CancellationToken ct)
    {
        var ids = content.BulletGroups().SelectMany(g => g.Bullets)
            .Where(b => b.SourceBulletId is not null).Select(b => b.SourceBulletId!.Value).Distinct().ToArray();
        // Ids were already checked as the user's own by ValidateSourceIdsAsync.
        var sources = await db.ProfileBullets.AsNoTracking().Where(b => ids.Contains(b.Id))
            .Select(b => new { b.Id, b.Text, Translated = b.Translations.Where(t => t.Language == content.Language).Select(t => t.Text).FirstOrDefault() })
            .ToDictionaryAsync(b => b.Id, ct);
        return TailoredCvWording.KeepSourceWording(content, mode,
            id => [sources[id].Text, sources[id].Translated ?? ""],
            id => sources[id].Translated ?? sources[id].Text, listingText, []);
    }
}
