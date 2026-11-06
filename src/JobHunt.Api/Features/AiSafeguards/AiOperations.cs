using JobHunt.Api.Data;
using JobHunt.Api.Features.Parsing;
using JobHunt.Api.Llm;
using Microsoft.EntityFrameworkCore;

namespace JobHunt.Api.Features.AiSafeguards;

public record AiOutcome(bool Success, string? ErrorCode = null, ParseJob? Job = null);

public class AiOperations(IDbContextFactory<AppDbContext> contexts, AiRequestDigests digests, TimeProvider time)
{
    public AiOperation? Current { get; private set; }
    private int ordinal;
    public int NextOrdinal() => Interlocked.Increment(ref ordinal) - 1;

    public async Task<T> RunAsync<T>(string kind, object arguments, Guid? requestId, AppDbContext caller,
        Func<Task<ResolvedLlmModel>> resolve, Func<ResolvedLlmModel, Task<T>> run,
        Func<T, AiOutcome> outcome, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (requestId == Guid.Empty) throw new AiSafetyException("invalid-request-id");
        var owner = caller.CurrentUserId ?? throw new AiSafetyException("forbidden");
        var digest = digests.Hash(new { Version = 1, Kind = kind, Arguments = arguments });
        var id = requestId ?? Guid.NewGuid();
        await using var db = await contexts.CreateDbContextAsync(ct);
        // Dedicated contexts also serve internal callers: IgnoreQueryFilters always has an explicit trusted owner.
        var receipts = db.AiOperations.IgnoreQueryFilters().Where(o => o.UserId == owner);
        var previous = await receipts.AsNoTracking().SingleOrDefaultAsync(o => o.RequestId == id, ct);
        if (previous is not null) return await ReplayAsync<T>(previous, digest, owner, caller, ct);
        var model = await resolve();
        AiOperation receipt;
        try
        {
            await using var transaction = await AiTransactions.BeginAsync(db, ct);
            previous = await receipts.AsNoTracking().SingleOrDefaultAsync(o => o.RequestId == id, ct);
            if (previous is not null) return await ReplayAsync<T>(previous, digest, owner, caller, ct);
            var active = await receipts.AsNoTracking().SingleOrDefaultAsync(o => o.Status == AiOperationStatus.Running, ct);
            if (active is not null)
                throw new AiSafetyException(active.Kind == kind && active.Provider == model.Provider
                    && active.Model == model.ModelId && active.CredentialId == model.CredentialId
                    && digests.Matches(owner, active.ProtectedRequestDigest, digest) ? "operation-in-progress" : "ai-busy");
            receipt = new()
            {
                RequestId = id, UserId = owner, Kind = kind, ProtectedRequestDigest = digests.Protect(owner, digest),
                Provider = model.Provider, Model = model.ModelId, CredentialId = model.CredentialId,
                Status = AiOperationStatus.Running, CreatedAt = time.GetUtcNow().UtcDateTime,
            };
            db.AiOperations.Add(receipt);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (Exception ex) when (SqliteErrors.IsContention(ex)) { throw new AiSafetyException("ai-busy"); }

        Current = receipt;
        ordinal = 0;
        var complete = false;
        try
        {
            var result = await run(model);
            var finished = outcome(result);
            // Commit receipt completion and any pending listing job in one caller transaction.
            caller.Attach(receipt);
            receipt.Status = finished.Success ? AiOperationStatus.Succeeded : AiOperationStatus.Failed;
            receipt.ErrorCode = finished.ErrorCode;
            receipt.ParseJobId = finished.Job?.Id;
            receipt.CompletedAt = time.GetUtcNow().UtcDateTime;
            await caller.SaveChangesAsync(ct);
            complete = true;
            return result;
        }
        finally
        {
            Current = null;
            if (!complete)
            {
                await using var recovery = await contexts.CreateDbContextAsync(CancellationToken.None);
                await using var transaction = await AiTransactions.BeginAsync(recovery, CancellationToken.None);
                await recovery.LlmUsages.IgnoreQueryFilters().Where(u => u.AiOperationId == receipt.Id
                    && u.UserId == owner && u.ErrorCode == "attempt-pending")
                    .ExecuteUpdateAsync(s => s.SetProperty(u => u.ErrorCode, "operation-interrupted"), CancellationToken.None);
                await recovery.AiOperations.IgnoreQueryFilters().Where(o => o.Id == receipt.Id && o.UserId == owner
                    && o.Status == AiOperationStatus.Running).ExecuteUpdateAsync(s => s
                        .SetProperty(o => o.Status, AiOperationStatus.Interrupted)
                        .SetProperty(o => o.ErrorCode, "operation-interrupted")
                        .SetProperty(o => o.CompletedAt, time.GetUtcNow().UtcDateTime), CancellationToken.None);
                await transaction.CommitAsync(CancellationToken.None);
            }
        }
    }

    private async Task<T> ReplayAsync<T>(AiOperation receipt, string digest, string owner, AppDbContext caller, CancellationToken ct)
    {
        if (!digests.Matches(owner, receipt.ProtectedRequestDigest, digest)) throw new AiSafetyException("request-conflict");
        if (receipt.Status == AiOperationStatus.Running) throw new AiSafetyException("operation-in-progress");
        if (receipt.Status == AiOperationStatus.Interrupted) throw new AiSafetyException("operation-interrupted");
        if (typeof(T) == typeof(ParseJob) && receipt.ParseJobId is { } jobId)
        {
            var job = await caller.ParseJobs.SingleOrDefaultAsync(j => j.Id == jobId, ct);
            if (job is not null) return (T)(object)job;
        }
        throw new AiSafetyException("result-unavailable");
    }

    public static async Task RecoverAsync(AppDbContext db, TimeProvider time, CancellationToken ct = default)
    {
        // Startup only, before serving requests, under the application's single-instance deployment contract.
        await using var transaction = await AiTransactions.BeginAsync(db, ct);
        var stale = db.AiOperations.IgnoreQueryFilters().Where(o => o.Status == AiOperationStatus.Running);
        await db.LlmUsages.IgnoreQueryFilters().Where(u => u.ErrorCode == "attempt-pending"
            && stale.Any(o => o.Id == u.AiOperationId && o.UserId == u.UserId))
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.ErrorCode, "operation-interrupted"), ct);
        await stale.ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, AiOperationStatus.Interrupted)
            .SetProperty(o => o.ErrorCode, "operation-interrupted")
            .SetProperty(o => o.CompletedAt, time.GetUtcNow().UtcDateTime), ct);
        await transaction.CommitAsync(ct);
    }
}
