using JobHunt.Api.Data;
using JobHunt.Api.Features.Listings;
using JobHunt.Api.Features.LlmModels;
using JobHunt.Api.Features.Profiles;

namespace JobHunt.Api.Features.TailoredCvDrafts;

[MutationType]
public static class TailoredCvAnalysisMutations
{
    [Error<ListingNotFoundException>]
    [Error<ProfileEntryNotFoundException>]
    [Error<InvalidTailoredCvAnalysisException>]
    [Error<UnknownLlmModelException>]
    [UseMutationConvention(PayloadFieldName = "result")]
    public static Task<TailoredCvAnalysisResult> AnalyzeTailoredCvSectionAsync(
        int listingId, TailoredCvSection section, int? sourceEntryId, string? model, AppDbContext db,
        TailoredCvAnalyzer analyzer, CancellationToken ct) =>
        analyzer.AnalyzeAsync(listingId, section, sourceEntryId, model, db, ct);

    [Error<ListingNotFoundException>]
    [Error<InvalidTailoredCvAnalysisException>]
    [Error<UnknownLlmModelException>]
    [UseMutationConvention(PayloadFieldName = "analysis")]
    public static Task<TailoredCvAnalysisReport> AnalyzeAllTailoredCvAsync(
        int listingId, string? model, AppDbContext db, TailoredCvAnalyzer analyzer, CancellationToken ct) =>
        analyzer.AnalyzeAllAsync(listingId, model, db, ct);
}
