using JobHunt.Api.Data;
using JobHunt.Api.Features.Listings;
using JobHunt.Api.Features.Profiles;

namespace JobHunt.Api.Features.TailoredCvDrafts;

[MutationType]
public static class TailoredCvAnalysisMutations
{
    [Error<ListingNotFoundException>]
    [Error<ProfileEntryNotFoundException>]
    [Error<InvalidTailoredCvAnalysisException>]
    [UseMutationConvention(PayloadFieldName = "result")]
    public static Task<TailoredCvAnalysisResult> AnalyzeTailoredCvSectionAsync(
        int listingId, TailoredCvSection section, int? sourceEntryId, AppDbContext db,
        TailoredCvAnalyzer analyzer, CancellationToken ct) =>
        analyzer.AnalyzeAsync(listingId, section, sourceEntryId, db, ct);

    [Error<ListingNotFoundException>]
    [Error<InvalidTailoredCvAnalysisException>]
    [UseMutationConvention(PayloadFieldName = "analysis")]
    public static Task<TailoredCvAnalysisReport> AnalyzeAllTailoredCvAsync(
        int listingId, AppDbContext db, TailoredCvAnalyzer analyzer, CancellationToken ct) =>
        analyzer.AnalyzeAllAsync(listingId, db, ct);
}
