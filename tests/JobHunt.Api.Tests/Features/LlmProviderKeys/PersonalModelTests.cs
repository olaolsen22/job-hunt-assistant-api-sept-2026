using System.Text.Json;
using JobHunt.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace JobHunt.Api.Tests.Features.LlmProviderKeys;

public class PersonalModelTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Models = "{ llmModels { defaultModel models { id provider modelId keySource credentialId } } }";
    private const string AnthropicModel = "anthropic:claude-haiku-4-5-20251001";
    private const string SetDefault = "mutation($model:String) { setDefaultLlmModel(input:{model:$model}) { llmModels { defaultModel } errors { __typename } } }";

    [Fact]
    public async Task ProviderKey_EnablesOnlyOwnersCatalog_AndRemovedDefaultFallsBack()
    {
        var owner = await factory.GetOwnerClientAsync();
        var other = await factory.GetSecondOwnerClientAsync();
        var guest = await factory.GetGuestClientAsync();
        await ProviderKeyTests.SetAsync(owner, "anthropic", "test-model-key");
        var allowed = (await owner.GraphQLAsync(Models)).GetProperty("llmModels").GetProperty("models");
        var anthropic = allowed.EnumerateArray().Single(m => m.GetProperty("provider").GetString() == "anthropic");
        Assert.Equal(AnthropicModel, anthropic.GetProperty("id").GetString());
        Assert.Equal("claude-haiku-4-5-20251001", anthropic.GetProperty("modelId").GetString());
        Assert.Equal("USER", anthropic.GetProperty("keySource").GetString());
        Assert.DoesNotContain((await other.GraphQLAsync(Models)).GetProperty("llmModels").GetProperty("models").EnumerateArray(),
            m => m.GetProperty("provider").GetString() == "anthropic");
        Assert.Single((await guest.GraphQLAsync(Models)).GetProperty("llmModels").GetProperty("models").EnumerateArray());
        await owner.GraphQLAsync(SetDefault, new { model = AnthropicModel });
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal("anthropic", (await db.UserLlmSettings.IgnoreQueryFilters().SingleAsync()).Provider);
        await ProviderKeyTests.RemoveAsync(owner, "anthropic");
        Assert.Equal(ApiFactory.DefaultModel, (await owner.GraphQLAsync(Models)).GetProperty("llmModels").GetProperty("defaultModel").GetString());
        var calls = factory.Providers.InferenceCalls;
        var rejected = await owner.GraphQLAsync("""
            mutation($model:String) { parseListing(input:{text:"job",mode:FULL,model:$model}) {
              parseJob { id } errors { __typename }
            } }
            """, new { model = AnthropicModel });
        Assert.Equal("UnknownLlmModelError", rejected.GetProperty("parseListing").GetProperty("errors")[0].GetProperty("__typename").GetString());
        Assert.Equal(calls, factory.Providers.InferenceCalls);
        await owner.GraphQLAsync(SetDefault, new { model = (string?)null });
    }

    [Fact]
    public async Task GeminiIdsStayCompatible_WhileKeyMetadataChanges()
    {
        var owner = await factory.GetOwnerClientAsync();
        var guest = await factory.GetGuestClientAsync();
        var key = (await ProviderKeyTests.SetAsync(owner, "gemini", "personal-gemini-key")).GetProperty("setProviderKey").GetProperty("providerKey").GetProperty("credentialId").GetString();
        var models = (await owner.GraphQLAsync(Models)).GetProperty("llmModels").GetProperty("models");
        Assert.All(models.EnumerateArray(), m =>
        {
            Assert.Equal(key, m.GetProperty("credentialId").GetString());
            Assert.Equal(m.GetProperty("id").GetString(), m.GetProperty("modelId").GetString());
        });
        var shared = (await guest.GraphQLAsync(Models)).GetProperty("llmModels").GetProperty("models")[0];
        Assert.Equal("shared-gemini", shared.GetProperty("credentialId").GetString());
        await ProviderKeyTests.RemoveAsync(owner, "gemini");
        Assert.All((await owner.GraphQLAsync(Models)).GetProperty("llmModels").GetProperty("models").EnumerateArray(),
            m => Assert.Equal("SHARED", m.GetProperty("keySource").GetString()));
    }
}
