using System.Text.Json;
using JobHunt.Api.Data;
using JobHunt.Api.Features.LlmProviderKeys;
using JobHunt.Api.Features.LlmModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace JobHunt.Api.Tests.Features.LlmProviderKeys;

public class PersonalModelTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Models = "{ llmModels { defaultModel models { id provider modelId keySource credentialId } } }";
    private const string OpenAiModel = "openai:gpt-6-luna";
    private const string SetDefault = "mutation($model:String) { setDefaultLlmModel(input:{model:$model}) { llmModels { defaultModel } errors { __typename } } }";

    [Fact]
    public async Task RemovedProvider_IsHiddenAndRejected_AndStoredDefaultFallsBack()
    {
        using var isolated = new ApiFactory();
        var owner = await isolated.GetOwnerClientAsync();
        using var scope = isolated.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.SingleAsync(u => u.Email == ApiFactory.OwnerEmail);
        const string removedModel = "anthropic:claude-haiku-4-5-20251001";
        db.UserProviderKeys.Add(new UserProviderKey
        {
            UserId = user.Id, Provider = "anthropic", ProtectedKey = "legacy-encrypted-key", Last4 = "1234",
        });
        db.UserLlmSettings.Add(new UserLlmSetting
        {
            UserId = user.Id, Provider = "anthropic", DefaultModel = removedModel,
        });
        await db.SaveChangesAsync();

        var models = (await owner.GraphQLAsync(Models)).GetProperty("llmModels");
        Assert.Equal(ApiFactory.DefaultModel, models.GetProperty("defaultModel").GetString());
        Assert.DoesNotContain(models.GetProperty("models").EnumerateArray(),
            m => m.GetProperty("provider").GetString() == "anthropic");
        var statuses = await owner.GraphQLAsync("{ providerKeys(includeRetired:true) { provider } }");
        Assert.DoesNotContain(statuses.GetProperty("providerKeys").EnumerateArray(),
            k => k.GetProperty("provider").GetString() == "anthropic");
        var calls = isolated.Providers.ValidationCalls;
        var rejectedKey = await ProviderKeyTests.SetAsync(owner, "anthropic", "removed-provider-key");
        Assert.Equal("invalid-input", rejectedKey.GetProperty("setProviderKey").GetProperty("errors")[0].GetProperty("code").GetString());
        Assert.Equal(calls, isolated.Providers.ValidationCalls);
        var rejected = await owner.GraphQLAsync("""
            mutation($model:String) { parseListing(input:{text:"job",mode:FULL,model:$model}) {
              parseJob { id } errors { __typename }
            } }
            """, new { model = removedModel });
        Assert.Equal("UnknownLlmModelError", rejected.GetProperty("parseListing").GetProperty("errors")[0].GetProperty("__typename").GetString());
        Assert.Equal(0, isolated.Providers.InferenceCalls);
        Assert.Equal(0, isolated.Llm.Calls);
    }

    [Fact]
    public async Task ProviderKey_EnablesOnlyOwnersCatalog_AndRemovedDefaultFallsBack()
    {
        var owner = await factory.GetOwnerClientAsync();
        var other = await factory.GetSecondOwnerClientAsync();
        var guest = await factory.GetGuestClientAsync();
        await ProviderKeyTests.SetAsync(owner, "openai", "test-model-key");
        var allowed = (await owner.GraphQLAsync(Models)).GetProperty("llmModels").GetProperty("models");
        var openaiModels = allowed.EnumerateArray().Where(m => m.GetProperty("provider").GetString() == "openai").ToList();
        Assert.Equal(4, openaiModels.Count);
        var openai = openaiModels.Single(m => m.GetProperty("id").GetString() == OpenAiModel);
        Assert.Equal("gpt-6-luna", openai.GetProperty("modelId").GetString());
        Assert.Equal("USER", openai.GetProperty("keySource").GetString());
        Assert.DoesNotContain((await other.GraphQLAsync(Models)).GetProperty("llmModels").GetProperty("models").EnumerateArray(),
            m => m.GetProperty("provider").GetString() == "openai");
        Assert.Single((await guest.GraphQLAsync(Models)).GetProperty("llmModels").GetProperty("models").EnumerateArray());
        await owner.GraphQLAsync(SetDefault, new { model = OpenAiModel });
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal("openai", (await db.UserLlmSettings.IgnoreQueryFilters().SingleAsync()).Provider);
        await ProviderKeyTests.RemoveAsync(owner, "openai");
        Assert.Equal(ApiFactory.DefaultModel, (await owner.GraphQLAsync(Models)).GetProperty("llmModels").GetProperty("defaultModel").GetString());
        var calls = factory.Providers.InferenceCalls;
        var rejected = await owner.GraphQLAsync("""
            mutation($model:String) { parseListing(input:{text:"job",mode:FULL,model:$model}) {
              parseJob { id } errors { __typename }
            } }
            """, new { model = OpenAiModel });
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
