using JobHunt.Api.Data;
using JobHunt.Api.Features.Auth;
using JobHunt.Api.Llm;
using Microsoft.EntityFrameworkCore;

namespace JobHunt.Api.Features.LlmProviderKeys;

public record ProviderKeyStatus(string Provider, bool Configured, string? CredentialId, string? Last4,
    DateTime? CreatedAt, DateTime? RetiredAt);

[QueryType]
public static class ProviderKeyQueries
{
    public static async Task<List<ProviderKeyStatus>> GetProviderKeysAsync(
        AppDbContext db, IHttpContextAccessor http, CancellationToken ct, bool includeRetired = false)
    {
        if (http.HttpContext?.User.IsInRole(Roles.Owner) != true)
            return [.. LlmProviders.Supported.Select(p => new ProviderKeyStatus(p, false, null, null, null, null))];
        var rows = await db.UserProviderKeys.AsNoTracking()
            .Where(k => includeRetired || k.RetiredAt == null).OrderBy(k => k.CreatedAt).ToListAsync(ct);
        var statuses = rows.Select(Status).ToList();
        statuses.AddRange(LlmProviders.Supported.Where(p => rows.All(k => k.Provider != p || k.RetiredAt != null))
            .Select(p => new ProviderKeyStatus(p, false, null, null, null, null)));
        return statuses;
    }

    internal static ProviderKeyStatus Status(UserProviderKey key) => new(key.Provider,
        key.RetiredAt is null, key.Id.ToString("D"), key.Last4, key.CreatedAt, key.RetiredAt);
}

[MutationType]
public static class ProviderKeyMutations
{
    [Error<ProviderKeyException>]
    [UseMutationConvention(PayloadFieldName = "providerKey")]
    public static async Task<ProviderKeyStatus> SetProviderKeyAsync(string provider, string apiKey,
        AppDbContext db, IHttpContextAccessor http, ProviderKeyValidator validator, ProviderKeyProtector protector,
        TimeProvider time, CancellationToken ct)
    {
        RequireOwner(http);
        apiKey = apiKey.Trim();
        await validator.ValidateAsync(provider, apiKey, ct);
        var key = new UserProviderKey
        {
            UserId = db.CurrentUserId!, Provider = provider,
            // A short malformed credential must never be echoed in full, even if metadata validation accepted it.
            Last4 = apiKey.Length > 4 ? apiKey[^4..] : "", CreatedAt = time.GetUtcNow().UtcDateTime,
        };
        key.ProtectedKey = protector.Protect(key, apiKey);
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await RetireAsync(provider, db, time, ct);
            db.UserProviderKeys.Add(key);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (Exception ex) when (SqliteErrors.IsContention(ex)) { throw new ProviderKeyException("conflict"); }
        return ProviderKeyQueries.Status(key);
    }

    [Error<ProviderKeyException>]
    [UseMutationConvention(PayloadFieldName = "providerKey")]
    public static async Task<ProviderKeyStatus> RemoveProviderKeyAsync(string provider, AppDbContext db,
        IHttpContextAccessor http, TimeProvider time, CancellationToken ct)
    {
        RequireOwner(http);
        if (!LlmProviders.Supported.Contains(provider)) throw new ProviderKeyException("invalid-input");
        try { await RetireAsync(provider, db, time, ct); }
        catch (Exception ex) when (SqliteErrors.IsContention(ex)) { throw new ProviderKeyException("conflict"); }
        return new(provider, false, null, null, null, null);
    }

    private static Task<int> RetireAsync(string provider, AppDbContext db, TimeProvider time, CancellationToken ct)
    {
        var now = time.GetUtcNow().UtcDateTime;
        return db.UserProviderKeys.Where(k => k.Provider == provider && k.RetiredAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(k => k.RetiredAt, now).SetProperty(k => k.ProtectedKey, (string?)null), ct);
    }

    private static void RequireOwner(IHttpContextAccessor http)
    {
        if (http.HttpContext?.User.IsInRole(Roles.Owner) != true) throw new ProviderKeyException("forbidden");
    }
}
