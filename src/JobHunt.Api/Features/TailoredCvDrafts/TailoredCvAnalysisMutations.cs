using JobHunt.Api.Data;
using JobHunt.Api.Features.AiSafeguards;
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
    [Error<AiSafetyException>]
    [UseMutationConvention(PayloadFieldName = "result")]
    public static Task<TailoredCvAnalysisResult> AnalyzeTailoredCvSectionAsync(
        int listingId, TailoredCvSection section, int? sourceEntryId, string? model, AppDbContext db,
        TailoredCvAnalyzer analyzer, CancellationToken ct, Guid? requestId = null) =>
        analyzer.AnalyzeAsync(listingId, section, sourceEntryId, model, db, ct, requestId);

    [Error<ListingNotFoundException>]
    [Error<InvalidTailoredCvAnalysisException>]
    [Error<UnknownLlmModelException>]
    [Error<AiSafetyException>]
    [UseMutationConvention(PayloadFieldName = "analysis")]
    public static Task<TailoredCvAnalysisReport> AnalyzeAllTailoredCvAsync(
        int listingId, string? model, AppDbContext db, TailoredCvAnalyzer analyzer, CancellationToken ct, Guid? requestId = null) =>
        analyzer.AnalyzeAllAsync(listingId, model, db, ct, requestId);
}
