using System.Text.Json;
using JobHunt.Api.Data;
using JobHunt.Api.Features.AiSafeguards;
using JobHunt.Api.Features.Listings;
using JobHunt.Api.Features.LlmModels;
using JobHunt.Api.Features.Profiles;
using JobHunt.Api.Llm;
using Microsoft.Extensions.AI;

namespace JobHunt.Api.Features.TailoredCvDrafts;

public enum TailoredCvSection { Experience, Education, Project, Skills, Summary }
public enum TailoredCvAnalysisStatus { Succeeded, Failed, Skipped }

// Null Technologies or Headline = no suggestion. Bullets: the text to use, in selection order.
public record TailoredCvAnalysisResult(TailoredCvAnalysisStatus Status, TailoredCvSection Section,
    int? SourceEntryId, List<int> SelectedBulletIds, List<int> SelectedSkillIds, string? Summary, string? Error,
    string? ErrorCode = null, string? Paragraph = null, List<string>? Technologies = null,
    List<TailoredCvBullet>? Bullets = null, string? Headline = null);

public record TailoredCvAnalysisReport(List<TailoredCvAnalysisResult> Results);

public class InvalidTailoredCvAnalysisException(string message) : Exception(message);

// Calls are deliberately bounded to one profile section so the current Flash model has a small task.
public class TailoredCvAnalyzer(
    LlmExecution execution,
    LlmModelResolver models, AiOperations operations)
{
    public const string Operation = "tailored-cv-section";
    public const int MaxContextLength = 100_000;
    public const int MaxOutputLength = 30_000;
    private const int MaxSectionsPerKind = 100;

    public Task<TailoredCvAnalysisResult> AnalyzeAsync(int listingId, TailoredCvSection section,
        int? sourceEntryId, string? model, AppDbContext db, CancellationToken ct, Guid? requestId = null,
        TailoredCvTemplate template = TailoredCvTemplate.Detailed, Language? language = null) =>
        operations.RunAsync("analyze-tailored-cv-section", new { listingId, section, sourceEntryId, model, template, language }, requestId, db,
            () => models.ResolveAsync(model, db, ct),
            async resolved => await AnalyzeSectionAsync(await LoadAsync(listingId, template, language, db, ct), section, sourceEntryId, resolved, db, ct),
            result => new AiOutcome(result.Status == TailoredCvAnalysisStatus.Succeeded, result.ErrorCode), ct);

    public Task<TailoredCvAnalysisReport> AnalyzeAllAsync(int listingId, string? model, AppDbContext db, CancellationToken ct,
        Guid? requestId = null, TailoredCvTemplate template = TailoredCvTemplate.Detailed, Language? language = null) =>
        operations.RunAsync("analyze-all-tailored-cv", new { listingId, model, template, language }, requestId, db,
            () => models.ResolveAsync(model, db, ct), resolved => AnalyzeAllCoreAsync(listingId, template, language, resolved, db, ct),
            report => new AiOutcome(report.Results.All(r => r.Status == TailoredCvAnalysisStatus.Succeeded),
                report.Results.FirstOrDefault(r => r.ErrorCode is not null)?.ErrorCode), ct);

    private record Sources(Listing Listing, Profile Profile, Language Language, TailoredCvTemplate Template);

    // Loaded once per request, so analyze-all doesn't reload the whole profile for every section.
    private static async Task<Sources> LoadAsync(int listingId, TailoredCvTemplate template, Language? language,
        AppDbContext db, CancellationToken ct)
    {
        var listing = await TailoredCvListingContext.LoadAsync(listingId, db, ct) ?? throw new ListingNotFoundException(listingId);
        return new(listing, await ProfileQueries.LoadAsync(db, ct), await TailoredCvLanguageText.TargetLanguageAsync(listing, language, db, ct),
            template);
    }

    private async Task<TailoredCvAnalysisResult> AnalyzeSectionAsync(Sources sources, TailoredCvSection section,
        int? sourceEntryId, ResolvedLlmModel model, AppDbContext db, CancellationToken ct, List<string>? cvBullets = null)
    {
        var (listing, profile, language, template) = sources;
        var input = JsonSerializer.Serialize(
            TailoredCvAnalysisContext.Build(listing, profile, section, sourceEntryId, language, cvBullets), TailoredCvJson.Options);
        if (input.Length > MaxContextLength)
            throw new InvalidTailoredCvAnalysisException($"Analysis input must be at most {MaxContextLength} characters.");

        var conciseCap = TailoredCvAnalysisInstructions.ConciseBulletCap(profile, section, sourceEntryId);
        var tooLong = false;
        var call = await execution.RunJsonAsync(model,
            [new(ChatRole.System, TailoredCvAnalysisInstructions.For(section, language, template, conciseCap)),
                new(ChatRole.User, $"<cv_context>\n{input}\n</cv_context>")],
            new ChatOptions { ResponseFormat = ChatResponseFormat.Json, Temperature = 0, MaxOutputTokens = 4096 },
            Operation, db, text => (tooLong = text.Length > MaxOutputLength)
                ? null : TailoredCvAnalysisDecoder.Decode(text, section, sourceEntryId, input, template),
            "The AI's answer couldn't be read. Try again, or use the external prompt.", ct);
        var error = tooLong ? "The AI's answer was too long to read. Try again." : call.Error!;
        return call.Value ?? Failed(section, sourceEntryId, error) with { ErrorCode = call.ErrorCode };
    }

    private async Task<TailoredCvAnalysisReport> AnalyzeAllCoreAsync(int listingId, TailoredCvTemplate template,
        Language? language, ResolvedLlmModel resolved, AppDbContext db, CancellationToken ct)
    {
        var sources = await LoadAsync(listingId, template, language, db, ct);
        // One request per entry keeps each prompt focused on a small, verifiable source section.
        List<(TailoredCvSection Section, int? Id)> sections =
        [
            .. EntrySections(TailoredCvSection.Experience, "work experiences", sources.Profile.Experiences.Select(e => e.Id)),
            .. EntrySections(TailoredCvSection.Education, "education entries", sources.Profile.Educations.Select(e => e.Id)),
            .. EntrySections(TailoredCvSection.Project, "projects", sources.Profile.Projects.Select(p => p.Id)),
            (TailoredCvSection.Skills, null),
            (TailoredCvSection.Summary, null),
        ];
        var results = new List<TailoredCvAnalysisResult>(sections.Count);
        string? stop = null;
        foreach (var (section, id) in sections)
        {
            ct.ThrowIfCancellationRequested();
            if (stop is not null) { results.Add(Skipped(section, id, stop)); continue; }
            // The summary runs last and sees the bullets the CV body will show, so the two agree.
            var cvBullets = section == TailoredCvSection.Summary
                ? results.SelectMany(r => r.Bullets ?? []).Select(b => b.Text).ToList()
                : null;
            TailoredCvAnalysisResult result;
            try { result = await AnalyzeSectionAsync(sources, section, id, resolved, db, ct, cvBullets); }
            catch (AiSafetyException ex) when (ex.Code == LlmErrorCodes.DailyLimitReached)
            { result = Skipped(section, id, ex.Code); }
            results.Add(result);
            if (LlmErrorCodes.StopsBatch(result.ErrorCode)) stop = result.ErrorCode;
        }
        return new(results);
    }

    private static IEnumerable<(TailoredCvSection, int?)> EntrySections(TailoredCvSection section, string noun, IEnumerable<int> ids)
    {
        var sorted = ids.Order().ToList();
        if (sorted.Count > MaxSectionsPerKind)
            throw new InvalidTailoredCvAnalysisException(
                $"Analyze all supports at most {MaxSectionsPerKind} {noun}. Analyze sections individually.");
        return sorted.Select(id => (section, (int?)id));
    }

    private static TailoredCvAnalysisResult Skipped(TailoredCvSection section, int? id, string code) =>
        new(TailoredCvAnalysisStatus.Skipped, section, id, [], [], null,
            code == LlmErrorCodes.DailyLimitReached ? AiSafetyException.MessageFor(code) : LlmErrorCodes.MessageFor(code), code);

    private static TailoredCvAnalysisResult Failed(TailoredCvSection section, int? sourceEntryId, string error) =>
        new(TailoredCvAnalysisStatus.Failed, section, sourceEntryId, [], [], null, error);
}
