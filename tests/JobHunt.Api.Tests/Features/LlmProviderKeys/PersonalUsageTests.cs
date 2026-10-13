using System.Net.Http.Json;
using System.Text.Json;
using JobHunt.Api.Data;
using JobHunt.Api.Llm;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace JobHunt.Api.Tests.Features.LlmProviderKeys;

public class PersonalUsageTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Today = "{ llmUsageToday { model provider keySource credentialId requests inputTokens outputTokens requestsLimit remainingRequests } }";
    private const string Daily = "query($key:String) { usageDaily(credentialId:$key) { model provider keySource credentialId requests inputTokens estimatedCost } }";
    private const string Parse = "mutation { parseListing(input:{text:\"An advertisement\",mode:FULL}) { parseJob { status } } }";

    [Fact]
    public async Task SameModel_OnTwoPersonalKeysAndSharedKey_HasSeparatePrivateTotalsAndRotationHistory()
    {
        var owner = await factory.GetOwnerClientAsync();
        var other = await factory.GetSecondOwnerClientAsync();
        var guest = await factory.GetGuestClientAsync();
        await ResetAsync();
        var oldId = await KeyAsync(owner, "gemini", "first-user-key");
        var otherId = await KeyAsync(other, "gemini", "second-user-key");
        factory.Providers.ResponseText = "{}";
        factory.Llm.Respond = () => new(new Microsoft.Extensions.AI.ChatMessage(Microsoft.Extensions.AI.ChatRole.Assistant, "{}"));
        try
        {
            await owner.GraphQLAsync(Parse); await owner.GraphQLAsync(Parse);
            await other.GraphQLAsync(Parse); await other.GraphQLAsync(Parse); await other.GraphQLAsync(Parse);
            await guest.GraphQLAsync(Parse);
            var personal = (await owner.GraphQLAsync(Today)).GetProperty("llmUsageToday");
            Assert.Equal(2, personal.GetProperty("requests").GetInt32());
            Assert.Equal(200, personal.GetProperty("inputTokens").GetInt64());
            Assert.Equal(oldId, personal.GetProperty("credentialId").GetString());
            Assert.Equal(JsonValueKind.Null, personal.GetProperty("requestsLimit").ValueKind);
            Assert.Equal(3, (await other.GraphQLAsync(Today)).GetProperty("llmUsageToday").GetProperty("requests").GetInt32());
            var shared = (await guest.GraphQLAsync(Today)).GetProperty("llmUsageToday");
            Assert.Equal(1, shared.GetProperty("requests").GetInt32());
            Assert.Equal("shared-gemini", shared.GetProperty("credentialId").GetString());
            Assert.Equal(500, shared.GetProperty("requestsLimit").GetInt32());
            var newId = await KeyAsync(owner, "gemini", "replacement-user-key");
            await owner.GraphQLAsync(Parse);
            Assert.Equal(1, (await owner.GraphQLAsync(Today)).GetProperty("llmUsageToday").GetProperty("requests").GetInt32());
            var history = (await owner.GraphQLAsync(Daily, new { key = (string?)null })).GetProperty("usageDaily");
            Assert.Equal(3, history.GetArrayLength());
            Assert.DoesNotContain(history.EnumerateArray(), r => r.GetProperty("credentialId").GetString() == otherId);
            Assert.Contains(history.EnumerateArray(), r => r.GetProperty("credentialId").GetString() == newId);
            var retired = (await owner.GraphQLAsync(Daily, new { key = oldId })).GetProperty("usageDaily");
            Assert.Single(retired.EnumerateArray());
            Assert.Equal(2, retired[0].GetProperty("requests").GetInt32());
            using var response = await owner.PostAsJsonAsync("/graphql", new { query = Daily, variables = new { key = otherId } });
            var rejected = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Contains("isn't available", rejected.GetProperty("errors")[0].GetProperty("message").GetString());
            await ProviderKeyTests.RemoveAsync(owner, "gemini");
            Assert.Equal(2, (await owner.GraphQLAsync(Daily, new { key = oldId })).GetProperty("usageDaily")[0].GetProperty("requests").GetInt32());
        }
        finally { await ProviderKeyTests.RemoveAsync(owner, "gemini"); await ProviderKeyTests.RemoveAsync(other, "gemini"); }
    }

    [Fact]
    public async Task SameNativeModelAcrossProviders_GroupsAndPricesSeparately()
    {
        var owner = await factory.GetOwnerClientAsync();
        await ResetAsync();
        var openai = await KeyAsync(owner, "openai", "priced-openai-key");
        var mistral = await KeyAsync(owner, "mistral", "priced-mistral-key");
        var user = await owner.GetFromJsonAsync<JsonElement>("/api/auth/me");
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        foreach (var (provider, credential) in new[] { ("openai", openai), ("mistral", mistral) })
            db.LlmUsages.Add(new()
            {
                UserId = user.GetProperty("id").GetString()!, Provider = provider, Model = "same-model",
                KeySource = LlmKeySource.User, UserProviderKeyId = Guid.Parse(credential), Operation = "test", CreatedAt = DateTime.UtcNow,
                InputTokens = 1000000, OutputTokens = 500000, Success = true,
            });
        await db.SaveChangesAsync();
        // Configure provider-specific prices through the same options used by the resolver.
        var prices = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<ProviderLlmPricingOptions>>().Value;
        prices["openai"] = new() { ["same-model"] = new() { InputPerMTok = 0.10m, OutputPerMTok = 0.40m } };
        prices["mistral"] = new() { ["same-model"] = new() { InputPerMTok = 0.20m, OutputPerMTok = 0.60m } };
        try
        {
            var rows = (await owner.GraphQLAsync(Daily, new { key = (string?)null })).GetProperty("usageDaily");
            Assert.Equal(2, rows.GetArrayLength());
            Assert.Equal(0.30m, rows.EnumerateArray().Single(r => r.GetProperty("provider").GetString() == "openai").GetProperty("estimatedCost").GetDecimal());
            Assert.Equal(0.50m, rows.EnumerateArray().Single(r => r.GetProperty("provider").GetString() == "mistral").GetProperty("estimatedCost").GetDecimal());
        }
        finally { await ProviderKeyTests.RemoveAsync(owner, "openai"); await ProviderKeyTests.RemoveAsync(owner, "mistral"); }
    }

    private static async Task<string> KeyAsync(HttpClient client, string provider, string key) =>
        (await ProviderKeyTests.SetAsync(client, provider, key)).GetProperty("setProviderKey").GetProperty("providerKey").GetProperty("credentialId").GetString()!;

    private async Task ResetAsync()
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().LlmUsages.IgnoreQueryFilters().ExecuteDeleteAsync();
    }
}
