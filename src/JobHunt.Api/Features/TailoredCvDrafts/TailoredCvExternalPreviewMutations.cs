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
        int listingId, string json, AppDbContext db, CancellationToken ct)
    {
        var listing = await db.Listings.Where(l => l.Id == listingId)
            .Select(l => new { l.PostingLanguage }).FirstOrDefaultAsync(ct)
            ?? throw new ListingNotFoundException(listingId);
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
        var content = TailoredCvDraftValidation.Normalize(parsed.Content, listing.PostingLanguage);
        await TailoredCvDraftValidation.ValidateSourceIdsAsync(content, db, ct, requireSourceBulletText: true);
        content = await TailoredCvDraftValidation.FillMissingLocationsAsync(content, db, ct);
        return new(parsed.SchemaVersion, content);
    }
}
