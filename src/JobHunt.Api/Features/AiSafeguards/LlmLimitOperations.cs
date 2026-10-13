using JobHunt.Api.Data;
using JobHunt.Api.Features.Auth;
using JobHunt.Api.Features.Usage;
using JobHunt.Api.Llm;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace JobHunt.Api.Features.AiSafeguards;

public record LlmCallLimit(string Provider, int? DailyRequestLimit, int RequestsConsumed, int? RemainingRequests, DateTime ResetsAt);

[QueryType]
public static class LlmLimitQueries
{
    public static async Task<List<LlmCallLimit>> GetLlmCallLimitsAsync(AppDbContext db, IHttpContextAccessor http,
        TimeProvider time, CancellationToken ct)
    {
        if (http.HttpContext?.User.IsInRole(Roles.Owner) != true) return [];
        var day = PacificDay.Of(time.GetUtcNow().UtcDateTime);
        var start = PacificDay.StartUtc(day);
        var end = PacificDay.StartUtc(day.AddDays(1));
        var limits = await db.UserLlmLimits.AsNoTracking().ToDictionaryAsync(l => l.Provider, ct);
        var counts = await db.LlmUsages.Where(u => u.KeySource == LlmKeySource.User && u.CreatedAt >= start && u.CreatedAt < end)
            .GroupBy(u => u.Provider).Select(g => new { Provider = g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Provider, ct);
        return [.. LlmProviders.Supported.Select(provider =>
        {
            var limit = limits.GetValueOrDefault(provider)?.DailyRequestLimit;
            var used = counts.GetValueOrDefault(provider)?.Count ?? 0;
            return new LlmCallLimit(provider, limit, used, limit is { } value ? Math.Max(0, value - used) : null, end);
        })];
    }
}

[MutationType]
public static class LlmLimitMutations
{
    [Error<AiSafetyException>]
    [UseMutationConvention(PayloadFieldName = "limit")]
    public static async Task<LlmCallLimit> SetLlmCallLimitAsync(string provider, int? dailyRequestLimit, AppDbContext db,
        IHttpContextAccessor http, TimeProvider time, CancellationToken ct)
    {
        if (http.HttpContext?.User.IsInRole(Roles.Owner) != true) throw new AiSafetyException("forbidden");
        if (!LlmProviders.Supported.Contains(provider) || dailyRequestLimit < 0) throw new AiSafetyException("invalid-input");
        try
        {
            await using var transaction = await AiTransactions.BeginAsync(db, ct);
            var limit = await db.UserLlmLimits.SingleOrDefaultAsync(l => l.Provider == provider, ct);
            if (limit is null) { limit = new() { UserId = db.CurrentUserId!, Provider = provider }; db.UserLlmLimits.Add(limit); }
            limit.DailyRequestLimit = dailyRequestLimit;
            limit.UpdatedAt = time.GetUtcNow().UtcDateTime;
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode is 5 or 6) { throw new AiSafetyException("ai-busy"); }
        return (await LlmLimitQueries.GetLlmCallLimitsAsync(db, http, time, ct)).Single(l => l.Provider == provider);
    }
}
