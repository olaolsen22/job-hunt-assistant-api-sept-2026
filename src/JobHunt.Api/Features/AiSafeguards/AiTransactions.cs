using JobHunt.Api.Data;
using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace JobHunt.Api.Features.AiSafeguards;

internal static class AiTransactions
{
    // Acquire the SQLite write lock before reading limits/receipts; no network work runs in this transaction.
    internal static async Task<IDbContextTransaction> BeginAsync(AppDbContext db, CancellationToken ct)
    {
        // EF's SQLite Serializable transaction uses non-deferred BeginTransaction and owns its lifetime.
        return await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
    }
}
