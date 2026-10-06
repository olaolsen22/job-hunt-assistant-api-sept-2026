using JobHunt.Api.Data;
using JobHunt.Api.Llm;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace JobHunt.Api.Features.Usage;

public record UsageToday(
    int Requests, long InputTokens, long OutputTokens, int? RequestsLimit, int? RemainingRequests, DateTime ResetsAt);

public record DailyUsage(
    DateOnly Day, string Model, int Requests, long InputTokens, long OutputTokens, decimal? EstimatedCost);

// Gemini's daily limit resets at midnight Pacific time, so "a day" here is a Pacific day, not a UTC one.
public static class PacificDay
{
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");

    public static DateOnly Of(DateTime utc) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc, Zone));

    // Converts the local midnight, so daylight saving shifts the UTC offset correctly. Midnight is never skipped
    // or repeated in Pacific time (the clock changes at 2am).
    public static DateTime StartUtc(DateOnly day) => TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(TimeOnly.MinValue), Zone);
}

// Both queries add up every user's calls with IgnoreQueryFilters: all users share one Gemini key, so the quota
// is shared too, and a per-user count would show a Guest "0 used" while the Owner's limit is gone. They return
// sums only, never another user's rows.
[QueryType]
public static class UsageQueries
{
    private const int MaxDays = 90;

    public static async Task<UsageToday> GetLlmUsageTodayAsync(
        AppDbContext db, TimeProvider time, IOptions<GeminiOptions> gemini, CancellationToken ct)
    {
        var today = PacificDay.Of(time.GetUtcNow().UtcDateTime);
        var start = PacificDay.StartUtc(today);
        var model = gemini.Value.Model;

        // Limits are per model, so another model's calls don't use up this one's.
        var totals = await db.LlmUsages.IgnoreQueryFilters()
            .Where(u => u.Model == model && u.CreatedAt >= start)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Requests = g.Count(),
                Input = g.Sum(u => u.InputTokens ?? 0),
                Output = g.Sum(u => u.OutputTokens ?? 0),
            })
            .FirstOrDefaultAsync(ct);

        var requests = totals?.Requests ?? 0;
        var limit = gemini.Value.RequestsPerDay;
        return new UsageToday(
            requests, totals?.Input ?? 0, totals?.Output ?? 0, limit,
            limit is { } l ? Math.Max(0, l - requests) : null,
            PacificDay.StartUtc(today.AddDays(1)));
    }

    // Grouped in memory: bucketing by Pacific day across a daylight-saving change is awkward in SQLite, and
    // 90 days of rows is small. Days and models without calls get no row.
    public static async Task<List<DailyUsage>> GetUsageDailyAsync(
        AppDbContext db, TimeProvider time, IOptions<LlmPricingOptions> pricing, CancellationToken ct, int days = 30)
    {
        if (days is < 1 or > MaxDays)
            throw new GraphQLException($"days must be between 1 and {MaxDays}.");

        var first = PacificDay.Of(time.GetUtcNow().UtcDateTime).AddDays(1 - days);
        var start = PacificDay.StartUtc(first);
        var rows = await db.LlmUsages.IgnoreQueryFilters()
            .Where(u => u.CreatedAt >= start)
            .Select(u => new { u.CreatedAt, u.Model, u.InputTokens, u.OutputTokens })
            .ToListAsync(ct);

        return [.. rows
            .GroupBy(r => (Day: PacificDay.Of(r.CreatedAt), r.Model))
            .OrderBy(g => g.Key.Day).ThenBy(g => g.Key.Model)
            .Select(g =>
            {
                var input = g.Sum(r => r.InputTokens ?? 0);
                var output = g.Sum(r => r.OutputTokens ?? 0);
                // No price entry means null, not 0, so a missing price can't look like a free model.
                decimal? cost = pricing.Value.TryGetValue(g.Key.Model, out var price)
                    ? (input * price.InputPerMTok + output * price.OutputPerMTok) / 1_000_000m
                    : null;
                return new DailyUsage(g.Key.Day, g.Key.Model, g.Count(), input, output, cost);
            })];
    }
}
