using System.Net;
using System.Text.Json;
using JobHunt.Api.Data;
using JobHunt.Api.Tests.Features.LlmProviderKeys;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace JobHunt.Api.Tests.Features.AiSafeguards;

public class BatchSafetyTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    internal const string All = """
        mutation($listing:Int!,$model:String,$id:UUID){analyzeAllTailoredCv(input:{listingId:$listing,model:$model,requestId:$id}){
          analysis{results{section status error errorCode}} errors{... on AiSafetyError{code}}
        }}
        """;
    internal static async Task<int> Listing(HttpClient owner)
    {
        var data = await owner.GraphQLAsync("""
            mutation($url:String!){createListing(input:{listing:{title:"Safeguard test",companyName:"Safeguards",jobRoleName:"Developer",
              url:$url,postedOn:"2026-10-01",deadlineType:UNSPECIFIED,skills:[],languages:[],locations:[]}}){listing{id}}}
            """, new { url = "https://example.com/" + Guid.NewGuid() });
        return data.GetProperty("createListing").GetProperty("listing").GetProperty("id").GetInt32();
    }

    [Theory]
    [InlineData(401, "invalid-key")]
    [InlineData(429, "rate-limited")]
    [InlineData(500, "provider-error")]
    [InlineData(408, "timeout")]
    public async Task SystemicFailureStopsAfterOneAttempt(int status, string code)
    {
        var owner = await factory.GetOwnerClientAsync(); var listing = await Listing(owner); var id = Guid.NewGuid();
        factory.Llm.Respond = () => throw new HttpRequestException("private failure", null, (HttpStatusCode)status);
        var before = factory.Llm.Calls;
        var results = (await owner.GraphQLAsync(All, new { listing, id })).GetProperty("analyzeAllTailoredCv").GetProperty("analysis").GetProperty("results");
        Assert.Equal("FAILED", results[0].GetProperty("status").GetString());
        Assert.Equal("SKIPPED", results[1].GetProperty("status").GetString());
        Assert.All(results.EnumerateArray(), r => Assert.Equal(code, r.GetProperty("errorCode").GetString()));
        Assert.Equal(before + 1, factory.Llm.Calls);
        Assert.Equal("result-unavailable", AiOperationTests.Code((await owner.GraphQLAsync(All, new { listing, id })).GetProperty("analyzeAllTailoredCv")));
    }

    [Fact]
    public async Task InvalidJsonContinuesAndEarlierSuccessSurvivesLaterFailure()
    {
        var owner = await factory.GetOwnerClientAsync(); var listing = await Listing(owner); var before = factory.Llm.Calls;
        var call = 0;
        factory.Llm.Respond = () => ++call == 1 ? new(new ChatMessage(ChatRole.Assistant, "invalid"))
            : new(new ChatMessage(ChatRole.Assistant, "{\"summary\":null}"));
        var results = (await owner.GraphQLAsync(All, new { listing })).GetProperty("analyzeAllTailoredCv").GetProperty("analysis").GetProperty("results");
        Assert.Equal("invalid-json", results[0].GetProperty("errorCode").GetString());
        Assert.Equal("SUCCEEDED", results[1].GetProperty("status").GetString()); Assert.Equal(before + 2, factory.Llm.Calls);
        call = 0;
        factory.Llm.Respond = () => ++call == 1 ? new(new ChatMessage(ChatRole.Assistant, "{\"selectedSkillIds\":[]}"))
            : throw new HttpRequestException("private", null, HttpStatusCode.TooManyRequests);
        results = (await owner.GraphQLAsync(All, new { listing })).GetProperty("analyzeAllTailoredCv").GetProperty("analysis").GetProperty("results");
        Assert.Equal("SUCCEEDED", results[0].GetProperty("status").GetString());
        Assert.Equal("FAILED", results[1].GetProperty("status").GetString());
    }

    [Fact]
    public async Task LimitExhaustionSkipsRemainingSectionsAndPreservesSuccess()
    {
        var owner = await factory.GetOwnerClientAsync(); var listing = await Listing(owner);
        await ProviderKeyTests.SetAsync(owner, "openai", "batch-limit-key"); factory.Providers.ResponseText = "{\"selectedSkillIds\":[]}";
        try
        {
            await LlmLimitTests.Set(owner, "openai", 1); var before = factory.Providers.InferenceCalls;
            var id = Guid.NewGuid();
            var results = (await owner.GraphQLAsync(All, new { listing, model = "openai:gpt-6-luna", id }))
                .GetProperty("analyzeAllTailoredCv").GetProperty("analysis").GetProperty("results");
            Assert.Equal("SUCCEEDED", results[0].GetProperty("status").GetString());
            Assert.Equal("SKIPPED", results[1].GetProperty("status").GetString());
            Assert.Equal("daily-limit-reached", results[1].GetProperty("errorCode").GetString());
            Assert.Equal(before + 1, factory.Providers.InferenceCalls);
            using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var receipt = await db.AiOperations.IgnoreQueryFilters().SingleAsync(o => o.RequestId == id);
            Assert.Single(await db.LlmUsages.IgnoreQueryFilters().Where(u => u.AiOperationId == receipt.Id).ToListAsync());
            Assert.Empty(await db.TailoredCvDrafts.IgnoreQueryFilters().ToListAsync());
        }
        finally { await LlmLimitTests.Set(owner, "openai", null); await ProviderKeyTests.RemoveAsync(owner, "openai"); }
    }
}
