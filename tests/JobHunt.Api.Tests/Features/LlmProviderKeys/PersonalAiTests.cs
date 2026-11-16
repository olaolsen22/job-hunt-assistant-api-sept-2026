using System.Net;
using System.Text.Json;
using JobHunt.Api.Data;
using JobHunt.Api.Llm;
using JobHunt.Api.Tests.Features.Profiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace JobHunt.Api.Tests.Features.LlmProviderKeys;

public class PersonalAiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static string Model(string provider) => provider switch
    {
        "gemini" => ApiFactory.DefaultModel,
        "openai" => "openai:gpt-4.1-mini",
        _ => "mistral:mistral-large-2512",
    };

    [Theory]
    [InlineData("gemini", "FULL")]
    [InlineData("openai", "FULL")]
    [InlineData("mistral", "FULL")]
    [InlineData("gemini", "QUICK")]
    [InlineData("openai", "COMPANIES")]
    public async Task ListingModes_UsePersonalCredential_AndNativeProviderModel(string provider, string mode)
    {
        var owner = await factory.GetOwnerClientAsync();
        var credential = (await ProviderKeyTests.SetAsync(owner, provider, "private-routing-key")).GetProperty("setProviderKey").GetProperty("providerKey").GetProperty("credentialId").GetString();
        try
        {
            factory.Providers.ResponseText = mode == "FULL" ? "{}" : "[]";
            var shared = factory.Llm.Calls;
            var payload = await owner.GraphQLAsync("""
                mutation($model:String!,$mode:ParseMode!) {
                  parseListing(input:{text:"A job advertisement",mode:$mode,model:$model}) {
                    parseJob { status provider model result error } errors { __typename }
                  }
                }
                """, new { model = Model(provider), mode });
            var job = payload.GetProperty("parseListing").GetProperty("parseJob");
            Assert.Equal("SUCCEEDED", job.GetProperty("status").GetString());
            Assert.Equal(provider, job.GetProperty("provider").GetString());
            Assert.Equal(provider == "gemini" ? Model(provider) : Model(provider)[(provider.Length + 1)..], job.GetProperty("model").GetString());
            Assert.Equal(shared, factory.Llm.Calls);
            var usage = await LatestAsync();
            Assert.Equal(Guid.Parse(credential!), usage.UserProviderKeyId);
            Assert.Equal(LlmKeySource.User, usage.KeySource);
            Assert.True(usage.Success);
        }
        finally { await ProviderKeyTests.RemoveAsync(owner, provider); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UrlMutations_KeepPersonalAttribution(bool saved)
    {
        var owner = await factory.GetOwnerClientAsync();
        await ProviderKeyTests.SetAsync(owner, "openai", "url-personal-key");
        try
        {
            factory.Pages.Respond = () => new(HttpStatusCode.OK) { Content = new StringContent("<html><body><p>We need a developer to build services. Build robust applications and collaborate with our engineering team. Build robust applications and collaborate with our engineering team. Build robust applications and collaborate with our engineering team. Build robust applications and collaborate with our engineering team. Build robust applications and collaborate with our engineering team. Build robust applications and collaborate with our engineering team. Build robust applications and collaborate with our engineering team. Build robust applications and collaborate with our engineering team. </p></body></html>") };
            factory.Providers.ResponseText = "{}";
            var id = await ListingAsync(owner);
            var query = saved ? """
                mutation($model:String,$id:Int!) { parseListingFromUrl(input:{listingId:$id,model:$model}) {
                  parseJob { status provider model } errors { __typename }
                } }
                """ : """
                mutation($model:String) { parseUrl(input:{url:"https://example.com/job",model:$model}) {
                  parseJob { status provider model } errors { __typename }
                } }
                """;
            var data = await owner.GraphQLAsync(query, saved ? new { model = Model("openai"), id } : (object)new { model = Model("openai") });
            var job = data.GetProperty(saved ? "parseListingFromUrl" : "parseUrl").GetProperty("parseJob");
            Assert.Equal("SUCCEEDED", job.GetProperty("status").GetString());
            Assert.Equal("openai", job.GetProperty("provider").GetString());
            Assert.Equal(LlmKeySource.User, (await LatestAsync()).KeySource);
        }
        finally { await ProviderKeyTests.RemoveAsync(owner, "openai"); }
    }

    [Fact]
    public async Task PersonalFailure_ReturnsFailedJob_ThenRemovalEnablesSharedGemini()
    {
        var owner = await factory.GetOwnerClientAsync();
        await ProviderKeyTests.SetAsync(owner, "gemini", "revoked-personal-key");
        factory.Providers.InferenceStatus = HttpStatusCode.Unauthorized;
        var shared = factory.Llm.Calls;
        const string query = "mutation { parseListing(input:{text:\"Job\",mode:FULL}) { parseJob { status error } } }";
        try
        {
            var failed = (await owner.GraphQLAsync(query)).GetProperty("parseListing").GetProperty("parseJob");
            Assert.Equal("FAILED", failed.GetProperty("status").GetString());
            Assert.Contains("API key", failed.GetProperty("error").GetString());
            Assert.Equal(shared, factory.Llm.Calls);
            Assert.Equal("invalid-key", (await LatestAsync()).ErrorCode);
        }
        finally { factory.Providers.InferenceStatus = HttpStatusCode.OK; await ProviderKeyTests.RemoveAsync(owner, "gemini"); }
        factory.Llm.Respond = () => new(new Microsoft.Extensions.AI.ChatMessage(Microsoft.Extensions.AI.ChatRole.Assistant, "{}"));
        await owner.GraphQLAsync(query);
        Assert.Equal(shared + 1, factory.Llm.Calls);
        Assert.Equal(LlmKeySource.Shared, (await LatestAsync()).KeySource);
    }

    [Fact]
    public async Task PersonalCv_SendsCompleteText_WithoutSavingRawContent()
    {
        var owner = await factory.GetOwnerClientAsync();
        await ProviderKeyTests.SetAsync(owner, "openai", "cv-personal-key");
        try
        {
            factory.Providers.ResponseText = ProfileCvCandidateTests.Empty;
            var text = new string('x', 99980) + "PRIVATE_CV_END";
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var jobs = await db.ParseJobs.IgnoreQueryFilters().CountAsync();
            var before = (await owner.GraphQLAsync("{ profile { fullName } }")).ToString();
            var data = await owner.GraphQLAsync("""
                mutation($text:String!,$model:String!) { parseProfileCv(input:{text:$text,model:$model}) {
                  result { status error } errors { __typename }
                } }
                """, new { text, model = Model("openai") });
            Assert.Equal("SUCCEEDED", data.GetProperty("parseProfileCv").GetProperty("result").GetProperty("status").GetString());
            using var body = JsonDocument.Parse(factory.Providers.LastBody!);
            Assert.Contains(text, body.RootElement.GetProperty("messages").ToString());
            Assert.Equal(jobs, await db.ParseJobs.IgnoreQueryFilters().CountAsync());
            Assert.Equal(before, (await owner.GraphQLAsync("{ profile { fullName } }")).ToString());
            Assert.Equal("openai", (await LatestAsync()).Provider);
        }
        finally { await ProviderKeyTests.RemoveAsync(owner, "openai"); }
    }

    [Fact]
    public async Task TailoredSectionAndAll_AttributeEveryAttemptWithoutSavingDrafts()
    {
        var owner = await factory.GetOwnerClientAsync();
        await ProviderKeyTests.SetAsync(owner, "openai", "analysis-personal-key");
        try
        {
            var id = await ListingAsync(owner);
            factory.Providers.ResponseText = "{\"selectedSkillIds\":[]}";
            var result = await owner.GraphQLAsync("""
                mutation($id:Int!,$model:String!) { analyzeTailoredCvSection(input:{listingId:$id,section:SKILLS,model:$model}) {
                  result { status error } errors { __typename }
                } }
                """, new { id, model = Model("openai") });
            Assert.Equal("SUCCEEDED", result.GetProperty("analyzeTailoredCvSection").GetProperty("result").GetProperty("status").GetString());
            factory.Providers.InferenceStatus = HttpStatusCode.Unauthorized;
            var calls = factory.Providers.InferenceCalls;
            var shared = factory.Llm.Calls;
            var data = await owner.GraphQLAsync("""
                mutation($id:Int!,$model:String!) { analyzeAllTailoredCv(input:{listingId:$id,model:$model}) {
                  analysis { results { status error } } errors { __typename }
                } }
                """, new { id, model = Model("openai") });
            var results = data.GetProperty("analyzeAllTailoredCv").GetProperty("analysis").GetProperty("results");
            Assert.Equal(2, results.GetArrayLength());
            Assert.Equal("FAILED", results[0].GetProperty("status").GetString());
            Assert.Equal("SKIPPED", results[1].GetProperty("status").GetString());
            Assert.Equal(calls + 1, factory.Providers.InferenceCalls);
            Assert.Equal(shared, factory.Llm.Calls);
            using var scope = factory.Services.CreateScope();
            Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<AppDbContext>().TailoredCvDrafts.IgnoreQueryFilters().CountAsync());
        }
        finally { factory.Providers.InferenceStatus = HttpStatusCode.OK; await ProviderKeyTests.RemoveAsync(owner, "openai"); }
    }

    private async Task<LlmUsage> LatestAsync()
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().LlmUsages.IgnoreQueryFilters().OrderByDescending(u => u.Id).FirstAsync();
    }

    private static async Task<int> ListingAsync(HttpClient owner)
    {
        var data = await owner.GraphQLAsync("""
            mutation($url:String!) { createListing(input:{listing:{title:"Provider test",companyName:"Provider tests",jobRoleName:"Developer",
              url:$url,postedOn:"2026-10-01",deadlineType:UNSPECIFIED,skills:[],languages:[],locations:[]}}) { listing { id } } }
            """, new { url = "https://example.com/" + Guid.NewGuid() });
        return data.GetProperty("createListing").GetProperty("listing").GetProperty("id").GetInt32();
    }
}
