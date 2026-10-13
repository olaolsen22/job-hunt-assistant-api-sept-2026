using System.Security.Claims;
using JobHunt.Api.Data;
using JobHunt.Api.Features.AiSafeguards;
using JobHunt.Api.Features.Auth;
using JobHunt.Api.Features.Parsing;
using JobHunt.Api.Features.Usage;
using JobHunt.Api.Llm;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace JobHunt.Api.Tests.Data;

public class AiSafetyStateTests : IAsyncLifetime
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private AppDbContext db = null!;
    private readonly TestHttp http = new();
    public async Task InitializeAsync()
    {
        await connection.OpenAsync();
        http.HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, "first"), new Claim(ClaimTypes.Role, "Owner")], "test")) };
        db = new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options, http);
        await db.Database.EnsureCreatedAsync();
        db.Users.AddRange(new AppUser { Id = "first", UserName = "first" }, new AppUser { Id = "second", UserName = "second" });
        await db.SaveChangesAsync();
    }

    private sealed class TestHttp : IHttpContextAccessor { public HttpContext? HttpContext { get; set; } }

    private static AiOperation Receipt(string user = "first") => new()
    {
        UserId = user, RequestId = Guid.NewGuid(), Kind = "test", ProtectedRequestDigest = "protected",
        Provider = "gemini", Model = "test", CredentialId = "shared-gemini", CreatedAt = DateTime.UtcNow,
    };

    [Fact]
    public async Task ActiveUniquenessAndUserIsolation()
    {
        var first = Receipt();
        var second = Receipt("second"); second.RequestId = first.RequestId;
        db.AiOperations.AddRange(first, second); await db.SaveChangesAsync();
        Assert.Single(await db.AiOperations.ToListAsync());
        Assert.Equal(2, await db.AiOperations.IgnoreQueryFilters().CountAsync());
        db.AiOperations.Add(Receipt());
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        await db.AiOperations.ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, AiOperationStatus.Succeeded));
        db.AiOperations.Add(Receipt()); await db.SaveChangesAsync();
    }

    [Fact]
    public async Task ReceiptCannotReferenceForeignJob()
    {
        var job = new ParseJob { Id = Guid.NewGuid(), UserId = "second", RawText = "test", Provider = "gemini" };
        db.ParseJobs.Add(job); await db.SaveChangesAsync();
        var receipt = Receipt(); receipt.ParseJobId = job.Id;
        db.AiOperations.Add(receipt);
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task UsageCannotReferenceForeignOperationAndOrdinalCannotRepeat()
    {
        var receipt = Receipt("second"); db.AiOperations.Add(receipt); await db.SaveChangesAsync();
        var usage = new LlmUsage { UserId = "first", Provider = "gemini", Model = "test", Operation = "test",
            AiOperationId = receipt.Id, AttemptOrdinal = 0, CreatedAt = DateTime.UtcNow };
        db.LlmUsages.Add(usage);
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear(); usage.UserId = "second";
        db.LlmUsages.Add(usage); await db.SaveChangesAsync();
        db.LlmUsages.Add(new() { UserId = "second", Provider = "gemini", Model = "test", Operation = "test",
            AiOperationId = receipt.Id, AttemptOrdinal = 0, CreatedAt = DateTime.UtcNow });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task LimitConstraintAndOwnership()
    {
        db.UserLlmLimits.AddRange(new() { UserId = "first", Provider = "openai", DailyRequestLimit = 5 },
            new() { UserId = "second", Provider = "openai", DailyRequestLimit = 10 });
        await db.SaveChangesAsync();
        Assert.Equal(5, (await db.UserLlmLimits.SingleAsync()).DailyRequestLimit);
        db.UserLlmLimits.Add(new() { UserId = "first", Provider = "mistral", DailyRequestLimit = -1 });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task ReportUsesPacificResetAcrossDaylightSavingAndHistoricalCredentials()
    {
        var start = new DateTime(2026, 11, 1, 7, 0, 0, DateTimeKind.Utc); // midnight PDT; next midnight is PST
        var key = new JobHunt.Api.Features.LlmProviderKeys.UserProviderKey { UserId = "first", Provider = "openai",
            ProtectedKey = "protected", Last4 = "1234", CreatedAt = start };
        db.UserProviderKeys.Add(key); db.UserLlmLimits.Add(new() { UserId = "first", Provider = "openai", DailyRequestLimit = 3 });
        await db.SaveChangesAsync();
        db.LlmUsages.AddRange(new[] { start.AddSeconds(-1), start, start.AddHours(24), start.AddHours(25) }.Select(date => new LlmUsage
        {
            UserId = "first", Provider = "openai", Model = "test", Operation = "test", KeySource = LlmKeySource.User,
            UserProviderKeyId = key.Id, CreatedAt = date,
        }));
        await db.SaveChangesAsync();
        var report = (await LlmLimitQueries.GetLlmCallLimitsAsync(db, http, new FixedTime(start.AddHours(2)), default))
            .Single(l => l.Provider == "openai");
        Assert.Equal(2, report.RequestsConsumed); Assert.Equal(1, report.RemainingRequests);
        Assert.Equal(start.AddHours(25), report.ResetsAt);
        var tomorrow = (await LlmLimitQueries.GetLlmCallLimitsAsync(db, http, new FixedTime(start.AddHours(25)), default))
            .Single(l => l.Provider == "openai");
        Assert.Equal(1, tomorrow.RequestsConsumed); Assert.Equal(2, tomorrow.RemainingRequests);
    }

    private sealed class FixedTime(DateTime utc) : TimeProvider { public override DateTimeOffset GetUtcNow() => new(utc); }

    [Fact]
    public void DigestsSurviveReconstructionWithoutRetainingCvContent()
    {
        var provider = new EphemeralDataProtectionProvider();
        var first = new AiRequestDigests(provider);
        var input = new { text = "private CV content", model = (string?)null, promptVersion = 1 };
        var digest = first.Hash(input);
        var value = first.Protect("first", digest);
        var second = new AiRequestDigests(provider);
        Assert.True(second.Matches("first", value, second.Hash(input)));
        Assert.False(second.Matches("first", value, second.Hash(input with { model = "explicit" })));
        Assert.False(second.Matches("first", value, second.Hash(input with { promptVersion = 2 })));
        Assert.DoesNotContain(input.text, value);
        Assert.DoesNotContain(digest, value);
        Assert.Equal("operation-interrupted", Assert.Throws<AiSafetyException>(() => second.Matches("second", value, digest)).Code);
    }

    public async Task DisposeAsync() { await db.DisposeAsync(); await connection.DisposeAsync(); }
}
