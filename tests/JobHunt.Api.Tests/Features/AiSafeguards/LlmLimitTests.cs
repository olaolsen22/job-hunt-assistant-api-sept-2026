using System.Text.Json;
using JobHunt.Api.Data;
using JobHunt.Api.Features.LlmProviderKeys;
using JobHunt.Api.Llm;
using JobHunt.Api.Tests.Features.LlmProviderKeys;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace JobHunt.Api.Tests.Features.AiSafeguards;

public class LlmLimitTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Query = "{llmCallLimits{provider dailyRequestLimit requestsConsumed remainingRequests resetsAt}}";
    internal static Task<JsonElement> Set(HttpClient client, string provider, int? limit) => client.GraphQLAsync("""
        mutation($provider:String!,$limit:Int){setLlmCallLimit(input:{provider:$provider,dailyRequestLimit:$limit}){
          limit{provider dailyRequestLimit requestsConsumed remainingRequests} errors{... on AiSafetyError{code}}
        }}
        """, new { provider, limit });
    private static JsonElement For(JsonElement data, string provider) => data.GetProperty("llmCallLimits").EnumerateArray()
        .Single(l => l.GetProperty("provider").GetString() == provider);
    internal static Task<JsonElement> PersonalParse(HttpClient owner, Guid? id = null) => owner.GraphQLAsync("""
        mutation($id:UUID){parseListing(input:{text:"Personal provider posting",mode:FULL,model:"openai:gpt-6-luna",requestId:$id}){
          parseJob{status} errors{... on AiSafetyError{code}}
        }}
        """, new { id });

    [Fact]
    public async Task SettingsArePrivateValidatedAndDisabledByDefault()
    {
        var owner = await factory.GetOwnerClientAsync(); var other = await factory.GetSecondOwnerClientAsync(); var guest = await factory.GetGuestClientAsync();
        Assert.Equal(JsonValueKind.Null, For(await owner.GraphQLAsync(Query), "mistral").GetProperty("dailyRequestLimit").ValueKind);
        try
        {
            var saved = (await Set(owner, "mistral", 10)).GetProperty("setLlmCallLimit").GetProperty("limit");
            Assert.Equal(10, saved.GetProperty("dailyRequestLimit").GetInt32());
            Assert.Equal(JsonValueKind.Null, For(await other.GraphQLAsync(Query), "mistral").GetProperty("dailyRequestLimit").ValueKind);
            Assert.Empty((await guest.GraphQLAsync(Query)).GetProperty("llmCallLimits").EnumerateArray());
            Assert.Equal("forbidden", AiOperationTests.Code((await Set(guest, "mistral", 5)).GetProperty("setLlmCallLimit")));
            Assert.Equal("invalid-input", AiOperationTests.Code((await Set(owner, "mistral", -1)).GetProperty("setLlmCallLimit")));
            Assert.Equal("invalid-input", AiOperationTests.Code((await Set(owner, "custom", 1)).GetProperty("setLlmCallLimit")));
        }
        finally { await Set(owner, "mistral", null); }
    }

    [Fact]
    public async Task PersonalLimitCountsExistingCallsAndKeyRotationButSharedGeminiIsExempt()
    {
        var owner = await factory.GetOwnerClientAsync(); var guest = await factory.GetGuestClientAsync();
        await ProviderKeyTests.SetAsync(owner, "openai", "first-limit-key");
        factory.Providers.ResponseText = "{}";
        try
        {
            var before = For(await owner.GraphQLAsync(Query), "openai").GetProperty("requestsConsumed").GetInt32();
            await Set(owner, "openai", before + 1);
            var id = Guid.NewGuid(); var calls = factory.Providers.InferenceCalls;
            await PersonalParse(owner, id);
            await PersonalParse(owner, id);
            Assert.Equal(calls + 1, factory.Providers.InferenceCalls);
            Assert.Equal("daily-limit-reached", AiOperationTests.Code((await PersonalParse(owner)).GetProperty("parseListing")));
            await ProviderKeyTests.SetAsync(owner, "openai", "rotated-limit-key");
            Assert.Equal("daily-limit-reached", AiOperationTests.Code((await PersonalParse(owner)).GetProperty("parseListing")));
            var counted = For(await owner.GraphQLAsync(Query), "openai");
            Assert.Equal(before + 1, counted.GetProperty("requestsConsumed").GetInt32());
            Assert.Equal(0, counted.GetProperty("remainingRequests").GetInt32());
            await Set(owner, "gemini", 0);
            factory.Llm.Respond = () => new(new Microsoft.Extensions.AI.ChatMessage(Microsoft.Extensions.AI.ChatRole.Assistant, "{}"));
            Assert.Equal("SUCCEEDED", (await guest.GraphQLAsync(AiOperationTests.Parse, new { text = "Free", id = Guid.NewGuid() }))
                .GetProperty("parseListing").GetProperty("parseJob").GetProperty("status").GetString());
            await ProviderKeyTests.SetAsync(owner, "gemini", "personal-gemini-limit-key");
            Assert.Equal("daily-limit-reached", AiOperationTests.Code((await owner.GraphQLAsync(AiOperationTests.Parse,
                new { text = "Personal Gemini", id = Guid.NewGuid() })).GetProperty("parseListing")));
            await Set(owner, "openai", null);
            await PersonalParse(owner);
            Assert.Equal(calls + 2, factory.Providers.InferenceCalls);
        }
        finally
        {
            await Set(owner, "openai", null); await Set(owner, "gemini", null);
            await ProviderKeyTests.RemoveAsync(owner, "openai"); await ProviderKeyTests.RemoveAsync(owner, "gemini");
        }
    }

    [Fact]
    public async Task EnablingLimitIncludesHistoricalModelsAndRetiredCredentials()
    {
        var owner = await factory.GetOwnerClientAsync();
        await ProviderKeyTests.SetAsync(owner, "mistral", "historical-key");
        try
        {
            using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = await db.Users.SingleAsync(u => u.Email == ApiFactory.OwnerEmail);
            var key = await db.UserProviderKeys.IgnoreQueryFilters().SingleAsync(k => k.UserId == user.Id && k.Provider == "mistral" && k.RetiredAt == null);
            var now = DateTime.UtcNow;
            db.LlmUsages.AddRange(Enumerable.Range(0, 7).Select(i => new LlmUsage
            {
                UserId = user.Id, Provider = "mistral", Model = i % 2 == 0 ? "old-model" : "new-model", Operation = "legacy",
                KeySource = LlmKeySource.User, UserProviderKeyId = key.Id, CreatedAt = now, Success = false,
            }));
            await db.SaveChangesAsync(); await ProviderKeyTests.RemoveAsync(owner, "mistral");
            var result = (await Set(owner, "mistral", 5)).GetProperty("setLlmCallLimit").GetProperty("limit");
            Assert.Equal(7, result.GetProperty("requestsConsumed").GetInt32());
            Assert.Equal(0, result.GetProperty("remainingRequests").GetInt32());
        }
        finally { await Set(owner, "mistral", null); await ProviderKeyTests.RemoveAsync(owner, "mistral"); }
    }

    [Fact]
    public async Task LastAllowanceIsReservedBeforeProviderReturnsAndConcurrentWorkCannotSpendItAgain()
    {
        var owner = await factory.GetOwnerClientAsync(); await ProviderKeyTests.SetAsync(owner, "openai", "contention-key");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        factory.Providers.BeforeInference = async ct => { entered.SetResult(); await release.Task.WaitAsync(ct); };
        factory.Providers.ResponseText = "{}";
        var before = For(await owner.GraphQLAsync(Query), "openai").GetProperty("requestsConsumed").GetInt32();
        await Set(owner, "openai", before + 1); var calls = factory.Providers.InferenceCalls;
        var pending = PersonalParse(owner, Guid.NewGuid());
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            var report = For(await owner.GraphQLAsync(Query), "openai");
            Assert.Equal(before + 1, report.GetProperty("requestsConsumed").GetInt32());
            Assert.Equal(0, report.GetProperty("remainingRequests").GetInt32());
            Assert.Equal("operation-in-progress", AiOperationTests.Code((await PersonalParse(owner, Guid.NewGuid())).GetProperty("parseListing")));
            await Set(owner, "openai", 0); // changing settings must not block on an open inference transaction
            Assert.Equal(calls + 1, factory.Providers.InferenceCalls);
        }
        finally
        {
            release.TrySetResult(); await pending; factory.Providers.BeforeInference = null;
            await Set(owner, "openai", null); await ProviderKeyTests.RemoveAsync(owner, "openai");
        }
    }
}
