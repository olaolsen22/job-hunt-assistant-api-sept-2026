using System.Net;
using System.Security.Claims;
using System.Text.Json;
using JobHunt.Api.Data;
using JobHunt.Api.Features.AiSafeguards;
using JobHunt.Api.Features.LlmProviderKeys;
using JobHunt.Api.Llm;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace JobHunt.Api.Tests.Features.LlmProviderKeys;

public class LlmExecutionTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Theory]
    [InlineData("gemini", 25)]
    [InlineData("anthropic", 20)]
    [InlineData("openai", 20)]
    [InlineData("mistral", 20)]
    public async Task PersonalExecution_MapsNativeModelOptionsAndTokens(string provider, int expectedOutput)
    {
        await factory.GetOwnerClientAsync();
        using var scope = factory.Services.CreateScope();
        await using var db = await SignedInDbAsync(scope);
        var protector = scope.ServiceProvider.GetRequiredService<ProviderKeyProtector>();
        var key = await AddAsync(db, protector, provider, "private-personal-key");
        var sharedCalls = factory.Llm.Calls;
        var result = await RunAsync(scope, scope.ServiceProvider.GetRequiredService<LlmExecution>(),
            new("picker-id", provider, "configured-model", "Model", key.Id),
            [new(ChatRole.System, "Return JSON"), new(ChatRole.User, "private-user-input")],
            new ChatOptions { ModelId = "incorrect-picker-id", ResponseFormat = ChatResponseFormat.Json,
                MaxOutputTokens = 32768, Temperature = 0, Reasoning = new() { Effort = ReasoningEffort.Low } }, "test", db, default);
        Assert.Null(result.ErrorCode);
        Assert.NotNull(result.Response);
        Assert.Equal(expectedOutput, result.Usage!.OutputTokens);
        Assert.Equal(100, result.Usage.InputTokens);
        Assert.Equal(key.Id, result.Usage.UserProviderKeyId);
        Assert.Equal("configured-model", result.Usage.Model);
        Assert.Equal(sharedCalls, factory.Llm.Calls);
        Assert.False(factory.Llm.Disposed);
        Assert.Equal("private-personal-key", factory.Providers.LastCredential);
        Assert.DoesNotContain("incorrect-picker-id", factory.Providers.LastBody);
        using var body = JsonDocument.Parse(factory.Providers.LastBody!);
        if (provider == "gemini")
        {
            Assert.Equal(32768, body.RootElement.GetProperty("generationConfig").GetProperty("maxOutputTokens").GetInt32());
            Assert.Equal("application/json", body.RootElement.GetProperty("generationConfig").GetProperty("responseMimeType").GetString());
        }
        else
        {
            Assert.Equal("configured-model", body.RootElement.GetProperty("model").GetString());
            var maxProperty = provider == "openai" ? "max_completion_tokens" : "max_tokens";
            Assert.Equal(32768, body.RootElement.GetProperty(maxProperty).GetInt32());
            Assert.False(body.RootElement.TryGetProperty("reasoning_effort", out _));
            if (provider != "anthropic") Assert.Equal("json_object", body.RootElement.GetProperty("response_format").GetProperty("type").GetString());
        }
        key.RetiredAt = DateTime.UtcNow; key.ProtectedKey = null;
        await db.SaveChangesAsync();
    }

    [Theory]
    [InlineData("gemini", 401, "invalid-key")]
    [InlineData("anthropic", 401, "invalid-key")]
    [InlineData("openai", 401, "invalid-key")]
    [InlineData("mistral", 401, "invalid-key")]
    [InlineData("gemini", 429, "rate-limited")]
    [InlineData("anthropic", 429, "rate-limited")]
    [InlineData("openai", 429, "rate-limited")]
    [InlineData("mistral", 429, "rate-limited")]
    public async Task PersonalFailure_DoesNotRetryOrFallback_AndLogsOnlySafeDetails(string provider, int status, string code)
    {
        await factory.GetOwnerClientAsync();
        using var scope = factory.Services.CreateScope();
        await using var db = await SignedInDbAsync(scope);
        var protector = scope.ServiceProvider.GetRequiredService<ProviderKeyProtector>();
        var key = await AddAsync(db, protector, provider, "private-error-key");
        var before = factory.Providers.InferenceCalls;
        var shared = factory.Llm.Calls;
        var logger = new CapturingLogger();
        factory.Providers.InferenceStatus = (HttpStatusCode)status;
        try
        {
            var execution = new LlmExecution(factory.Llm, scope.ServiceProvider.GetRequiredService<IHttpClientFactory>(), protector,
                TimeProvider.System, logger, scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>(),
                scope.ServiceProvider.GetRequiredService<JobHunt.Api.Features.AiSafeguards.AiOperations>());
            var result = await RunAsync(scope, execution, new("picker-id", provider, "configured-model", "Model", key.Id),
                [new(ChatRole.User, "private-input")], new() { MaxOutputTokens = 4096 }, "test", db, default);
            Assert.Equal(code, result.ErrorCode);
            Assert.False(result.Usage!.Success);
            Assert.Equal(key.Id, result.Usage.UserProviderKeyId);
            Assert.Equal(before + 1, factory.Providers.InferenceCalls);
            Assert.Equal(shared, factory.Llm.Calls);
            Assert.DoesNotContain("private-", string.Join(' ', logger.Messages));
            Assert.DoesNotContain("private", result.Error);
        }
        finally
        {
            factory.Providers.InferenceStatus = HttpStatusCode.OK;
            key.RetiredAt = DateTime.UtcNow; key.ProtectedKey = null;
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task UndecryptableOrForeignCredential_MakesNoProviderCall()
    {
        await factory.GetOwnerClientAsync();
        using var scope = factory.Services.CreateScope();
        await using var db = await SignedInDbAsync(scope);
        var key = new UserProviderKey { Provider = "gemini", UserId = db.CurrentUserId!, ProtectedKey = "corrupt", Last4 = "fake" };
        db.UserProviderKeys.Add(key); await db.SaveChangesAsync();
        var calls = factory.Providers.InferenceCalls;
        var shared = factory.Llm.Calls;
        foreach (var id in new[] { key.Id, Guid.NewGuid() })
        {
            var result = await RunAsync(scope, scope.ServiceProvider.GetRequiredService<LlmExecution>(),
                new("id", "gemini", "configured-model", "Model", id), [new(ChatRole.User, "input")], new(), "test", db, default);
            Assert.Equal("credential-unavailable", result.ErrorCode);
            Assert.Null(result.Usage);
        }
        Assert.Equal(calls, factory.Providers.InferenceCalls);
        Assert.Equal(shared, factory.Llm.Calls);
        key.RetiredAt = DateTime.UtcNow; key.ProtectedKey = null; await db.SaveChangesAsync();
    }

    [Fact]
    public async Task CancellationAfterAttempt_RecordsUsage_AndPropagatesCancellation()
    {
        await factory.GetOwnerClientAsync();
        using var scope = factory.Services.CreateScope();
        await using var db = await SignedInDbAsync(scope);
        using var cancel = new CancellationTokenSource();
        factory.Llm.Respond = () => { cancel.Cancel(); throw new OperationCanceledException("private-exception-details"); };
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => RunAsync(scope, scope.ServiceProvider.GetRequiredService<LlmExecution>(),
            new("id", "gemini", "model", "Model"), [new(ChatRole.User, "private-content")], new(), "canceled-test", db, cancel.Token));
        Assert.DoesNotContain("private", error.ToString());
        var row = await db.LlmUsages.IgnoreQueryFilters().SingleAsync(u => u.Operation == "canceled-test");
        Assert.False(row.Success);
        Assert.Equal("canceled", row.ErrorCode);
        Assert.Null(row.InputTokens);
    }

    [Fact]
    public async Task SharedClient_IsNeverDisposed_AndCancellationPropagates()
    {
        await factory.GetOwnerClientAsync();
        using var scope = factory.Services.CreateScope();
        await using var db = await SignedInDbAsync(scope);
        factory.Llm.Respond = () => new ChatResponse(new ChatMessage(ChatRole.Assistant, "{}"));
        var execution = scope.ServiceProvider.GetRequiredService<LlmExecution>();
        var result = await RunAsync(scope, execution, new("id", "gemini", "model", "Model"),
            [new(ChatRole.User, "input")], new(), "test", db, default);
        Assert.Null(result.ErrorCode);
        Assert.False(factory.Llm.Disposed);
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => RunAsync(scope, execution,
            new("id", "gemini", "model", "Model"), [new(ChatRole.User, "input")], new(), "test", db, cancel.Token));
    }

    // Provider calls always run inside an AI operation.
    private static Task<LlmCallResult> RunAsync(IServiceScope scope, LlmExecution execution, ResolvedLlmModel model,
        IEnumerable<ChatMessage> messages, ChatOptions options, string operation, AppDbContext db, CancellationToken ct) =>
        scope.ServiceProvider.GetRequiredService<AiOperations>().RunAsync(operation, new { }, null, db, () => Task.FromResult(model),
            selected => execution.RunAsync(selected, messages, options, operation, db, ct),
            result => new AiOutcome(result.ErrorCode is null, result.ErrorCode), ct);

    private static async Task<AppDbContext> SignedInDbAsync(IServiceScope scope)
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.SingleAsync(u => u.Email == ApiFactory.OwnerEmail);
        return new AppDbContext(scope.ServiceProvider.GetRequiredService<DbContextOptions<AppDbContext>>(),
            new TestContext { HttpContext = new DefaultHttpContext
            { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user.Id)], "test")) } });
    }

    private static async Task<UserProviderKey> AddAsync(AppDbContext db, ProviderKeyProtector protector, string provider, string secret)
    {
        var key = new UserProviderKey { UserId = db.CurrentUserId!, Provider = provider, Last4 = "fake" };
        key.ProtectedKey = protector.Protect(key, secret); db.UserProviderKeys.Add(key); await db.SaveChangesAsync();
        return key;
    }

    private sealed class TestContext : IHttpContextAccessor { public HttpContext? HttpContext { get; set; } }

    private sealed class CapturingLogger : ILogger<LlmExecution>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        { Assert.Null(exception); Messages.Add(formatter(state, exception)); }
    }
}
