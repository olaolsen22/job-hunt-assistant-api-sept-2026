using JobHunt.Api.Data;
using JobHunt.Api.Features.LlmModels;
using JobHunt.Api.Llm;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace JobHunt.Api.Features.Usage;

public record UsageToday(
    string Model, int Requests, long InputTokens, long OutputTokens, int? RequestsLimit, int? RemainingRequests, DateTime ResetsAt,
    string Provider, LlmKeySource KeySource, string CredentialId);

public record DailyUsage(
    DateOnly Day, string Model, int Requests, long InputTokens, long OutputTokens, decimal? EstimatedCost,
    string Provider, LlmKeySource KeySource, string CredentialId);

// Gemini's daily limit resets at midnight Pacific time, so "a day" here is a Pacific day, not a UTC one.
public static class PacificDay
{
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");

    public static DateOnly Of(DateTime utc) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc, Zone));

    // Converts local midnight, so daylight saving shifts the UTC offset correctly.
    public static DateTime StartUtc(DateOnly day) => TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(TimeOnly.MinValue), Zone);
}

// Shared usage is aggregated across users. Personal credential activity belongs only to its owner.
[QueryType]
public static class UsageQueries
{
    private const int MaxDays = 90;

    public static async Task<UsageToday> GetLlmUsageTodayAsync(
        AppDbContext db, TimeProvider time, LlmModelResolver models, CancellationToken ct)
    {
        var today = PacificDay.Of(time.GetUtcNow().UtcDateTime);
        var start = PacificDay.StartUtc(today);
        // The caller's effective default: the model their next AI call uses unless they pick another.
        var reported = await models.GetDefaultAsync(db, ct);
        var model = reported.ModelId;

        // Limits are per model, so another model's calls don't use up this one's.
        var totals = await db.LlmUsages.IgnoreQueryFilters()
            .Where(u => u.Model == model && u.Provider == reported.Provider && u.CreatedAt >= start
                && u.KeySource == reported.KeySource && u.UserProviderKeyId == reported.UserProviderKeyId
                && (reported.UserProviderKeyId == null || u.UserId == db.CurrentUserId))
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Requests = g.Count(),
                Input = g.Sum(u => u.InputTokens ?? 0),
                Output = g.Sum(u => u.OutputTokens ?? 0),
            })
            .FirstOrDefaultAsync(ct);

        var requests = totals?.Requests ?? 0;
        var limit = reported.RequestsPerDay;
        return new UsageToday(
            model, requests, totals?.Input ?? 0, totals?.Output ?? 0, limit,
            limit is { } l ? Math.Max(0, l - requests) : null,
            PacificDay.StartUtc(today.AddDays(1)), reported.Provider, reported.KeySource, reported.CredentialId);
    }

    // Grouped in memory: Pacific-day buckets across a DST change are awkward in SQLite, and 90 days of rows is small.
    public static async Task<List<DailyUsage>> GetUsageDailyAsync(
        AppDbContext db, TimeProvider time, IOptions<LlmPricingOptions> pricing,
        IOptions<ProviderLlmPricingOptions> providerPricing, CancellationToken ct, int days = 30, string? credentialId = null)
    {
        if (days is < 1 or > MaxDays)
            throw new GraphQLException($"days must be between 1 and {MaxDays}.");

        var first = PacificDay.Of(time.GetUtcNow().UtcDateTime).AddDays(1 - days);
        var start = PacificDay.StartUtc(first);
        var visible = db.LlmUsages.IgnoreQueryFilters().Where(u => u.CreatedAt >= start &&
            (u.KeySource == LlmKeySource.Shared && u.Provider == GeminiOptions.Provider && u.UserProviderKeyId == null
                || u.KeySource == LlmKeySource.User && u.UserId == db.CurrentUserId));
        if (credentialId == LlmProviders.SharedCredentialId)
            visible = visible.Where(u => u.KeySource == LlmKeySource.Shared);
        else if (credentialId is not null)
        {
            if (!Guid.TryParse(credentialId, out var keyId) || !await db.UserProviderKeys.AnyAsync(k => k.Id == keyId, ct))
                throw new GraphQLException("That credential isn't available.");
            visible = visible.Where(u => u.KeySource == LlmKeySource.User && u.UserProviderKeyId == keyId);
        }
        var rows = await visible
            .Select(u => new { u.CreatedAt, u.Provider, u.Model, u.KeySource, u.UserProviderKeyId, u.InputTokens, u.OutputTokens })
            .ToListAsync(ct);

        return [.. rows
            .GroupBy(r => (Day: PacificDay.Of(r.CreatedAt), r.Provider, r.Model, r.KeySource, r.UserProviderKeyId))
            .OrderBy(g => g.Key.Day).ThenBy(g => g.Key.Provider).ThenBy(g => g.Key.Model).ThenBy(g => g.Key.UserProviderKeyId)
            .Select(g =>
            {
                var input = g.Sum(r => r.InputTokens ?? 0);
                var output = g.Sum(r => r.OutputTokens ?? 0);
                // No price entry means null, not 0, so a missing price can't look like a free model.
                ModelPrice? price = null;
                if (providerPricing.Value.TryGetValue(g.Key.Provider, out var prices)) prices.TryGetValue(g.Key.Model, out price);
                if (price is null && g.Key.Provider == GeminiOptions.Provider) pricing.Value.TryGetValue(g.Key.Model, out price);
                decimal? cost = price is not null
                    ? (input * price.InputPerMTok + output * price.OutputPerMTok) / 1_000_000m
                    : null;
                return new DailyUsage(g.Key.Day, g.Key.Model, g.Count(), input, output, cost,
                    g.Key.Provider, g.Key.KeySource, g.Key.UserProviderKeyId?.ToString("D") ?? LlmProviders.SharedCredentialId);
            })];
    }
}
