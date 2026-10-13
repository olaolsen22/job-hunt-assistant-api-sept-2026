using System.Security.Claims;
using System.Net.Http.Json;
using System.Text.Json;
using JobHunt.Api.Data;
using JobHunt.Api.Features.AiSafeguards;
using JobHunt.Api.Llm;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace JobHunt.Api.Tests.Features.AiSafeguards;

public class AiRecoveryTests
{
    private sealed class Fault : SaveChangesInterceptor
    {
        public bool Enabled { get; set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (Enabled && data.Context!.ChangeTracker.Entries<AiOperation>().Any(e => e.Entity.Status == AiOperationStatus.Succeeded))
                throw new DbUpdateException("Simulated final-save failure");
            return ValueTask.FromResult(result);
        }
    }
    private sealed class DurableFactory(string connection, string ring, Fault? fault = null) : ApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("ConnectionStrings:Default", connection);
            builder.UseSetting("DataProtection:KeyRingPath", ring);
            if (fault is not null) builder.ConfigureServices(services =>
                services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(fault)));
        }
    }

    [Fact]
    public async Task RecreatedHostReplaysCompletedAndInterruptsPendingOperations()
    {
        var path = Path.Combine(Path.GetTempPath(), "ai-recovery-" + Guid.NewGuid()); Directory.CreateDirectory(path);
        var connection = "Data Source=" + Path.Combine(path, "test.db"); var ring = Path.Combine(path, "ring");
        var completed = Guid.NewGuid(); var pending = Guid.NewGuid(); string? originalJob;
        try
        {
            using (var first = new DurableFactory(connection, ring))
            {
                var owner = await first.GetOwnerClientAsync(); first.Llm.Respond = () => new(new ChatMessage(ChatRole.Assistant, "{}"));
                originalJob = (await owner.GraphQLAsync(AiOperationTests.Parse, new { text = "Durable", id = completed }))
                    .GetProperty("parseListing").GetProperty("parseJob").GetProperty("id").GetString();
                using var scope = first.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var user = await db.Users.SingleAsync(u => u.Email == ApiFactory.OwnerEmail);
                var digests = scope.ServiceProvider.GetRequiredService<AiRequestDigests>();
                var receipt = new AiOperation { UserId = user.Id, RequestId = pending, Kind = "parse-listing",
                    ProtectedRequestDigest = digests.Protect(user.Id, digests.Hash(new { Version = 1, Kind = "parse-listing",
                        Arguments = new { text = "Pending", mode = JobHunt.Api.Features.Parsing.ParseMode.Full, model = (string?)null } })),
                    Provider = "gemini", Model = ApiFactory.DefaultModel, CredentialId = "shared-gemini", CreatedAt = DateTime.UtcNow };
                db.AiOperations.Add(receipt); await db.SaveChangesAsync();
                db.LlmUsages.Add(new() { AiOperationId = receipt.Id, AttemptOrdinal = 0, UserId = user.Id, Provider = "gemini",
                    Model = ApiFactory.DefaultModel, Operation = "parse-listing", CreatedAt = DateTime.UtcNow, ErrorCode = "attempt-pending" });
                await db.SaveChangesAsync();
            }
            using (var second = new DurableFactory(connection, ring))
            {
                var owner = await second.GetOwnerClientAsync();
                var replay = (await owner.GraphQLAsync(AiOperationTests.Parse, new { text = "Durable", id = completed }))
                    .GetProperty("parseListing").GetProperty("parseJob").GetProperty("id").GetString();
                Assert.Equal(originalJob, replay); Assert.Equal(0, second.Llm.Calls);
                Assert.Equal("operation-interrupted", AiOperationTests.Code((await owner.GraphQLAsync(AiOperationTests.Parse,
                    new { text = "Pending", id = pending })).GetProperty("parseListing")));
                second.Llm.Respond = () => new(new ChatMessage(ChatRole.Assistant, "{}"));
                await owner.GraphQLAsync(AiOperationTests.Parse, new { text = "New explicit attempt", id = Guid.NewGuid() });
                Assert.Equal(1, second.Llm.Calls);
                using var scope = second.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var row = await db.LlmUsages.IgnoreQueryFilters().SingleAsync(u => u.ErrorCode == "operation-interrupted");
                Assert.False(row.Success);
            }
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); Directory.Delete(path, true); }
    }

    [Fact]
    public async Task FinalSaveFailureDoesNotRepeatPaidWorkOrKeepUserBusy()
    {
        var path = Path.Combine(Path.GetTempPath(), "ai-fault-" + Guid.NewGuid()); Directory.CreateDirectory(path);
        try
        {
            var fault = new Fault(); using var factory = new DurableFactory("Data Source=" + Path.Combine(path, "test.db"), Path.Combine(path, "ring"), fault);
            var owner = await factory.GetOwnerClientAsync(); var id = Guid.NewGuid();
            factory.Llm.Respond = () => new(new ChatMessage(ChatRole.Assistant, "{}")); fault.Enabled = true;
            var response = await owner.PostAsJsonAsync("/graphql", new { query = AiOperationTests.Parse, variables = new { text = "Fault", id } });
            var body = await response.Content.ReadFromJsonAsync<JsonElement>(); Assert.True(body.TryGetProperty("errors", out _));
            fault.Enabled = false;
            Assert.Equal("operation-interrupted", AiOperationTests.Code((await owner.GraphQLAsync(AiOperationTests.Parse,
                new { text = "Fault", id })).GetProperty("parseListing")));
            Assert.Equal(1, factory.Llm.Calls);
            await owner.GraphQLAsync(AiOperationTests.Parse, new { text = "Explicit next", id = Guid.NewGuid() });
            Assert.Equal(2, factory.Llm.Calls);
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); Directory.Delete(path, true); }
    }

    [Fact]
    public async Task BatchCancellationRecordsOnlyAttemptAndNeverCallsNextSection()
    {
        using var factory = new ApiFactory(); await factory.GetOwnerClientAsync();
        var owner = await factory.GetOwnerClientAsync(); var listing = await BatchSafetyTests.Listing(owner);
        using var scope = factory.Services.CreateScope(); var original = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await original.Users.SingleAsync(u => u.Email == ApiFactory.OwnerEmail);
        await using var caller = new AppDbContext(scope.ServiceProvider.GetRequiredService<DbContextOptions<AppDbContext>>(),
            new UserContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(ClaimTypes.NameIdentifier, user.Id), new Claim(ClaimTypes.Role, "Owner")], "test")) } });
        // The resolver reads the real request accessor; direct test calls select the allowed shared default explicitly.
        using var cancel = new CancellationTokenSource();
        factory.Llm.Respond = () => { cancel.Cancel(); throw new OperationCanceledException("private details"); };
        var operations = scope.ServiceProvider.GetRequiredService<AiOperations>();
        var execution = scope.ServiceProvider.GetRequiredService<LlmExecution>();
        var id = Guid.NewGuid(); var attempts = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operations.RunAsync("batch-cancellation", new { listing }, id, caller,
            () => Task.FromResult(new ResolvedLlmModel("id", "gemini", ApiFactory.DefaultModel, "Default")), async model =>
            {
                for (var i = 0; i < 2; i++) { attempts++; await execution.RunAsync(model, [new(ChatRole.User, "private input")], new(), "batch-cancellation", caller, cancel.Token); }
                return true;
            }, success => new AiOutcome(success), cancel.Token));
        Assert.Equal(1, attempts); Assert.Equal(1, factory.Llm.Calls);
        var receipt = await original.AiOperations.IgnoreQueryFilters().SingleAsync(o => o.RequestId == id);
        Assert.Equal(AiOperationStatus.Interrupted, receipt.Status);
        var row = await original.LlmUsages.IgnoreQueryFilters().SingleAsync(u => u.AiOperationId == receipt.Id);
        Assert.Equal("canceled", row.ErrorCode); Assert.False(row.Success);
    }

    private sealed class UserContext : IHttpContextAccessor { public HttpContext? HttpContext { get; set; } }
}
