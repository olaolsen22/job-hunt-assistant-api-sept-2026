using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using JobHunt.Api.Data;
using JobHunt.Api.Features.AiSafeguards;
using JobHunt.Api.Features.Profiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace JobHunt.Api.Tests.Features.AiSafeguards;

public class AiOperationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    internal const string Parse = """
        mutation($text:String!,$id:UUID){parseListing(input:{text:$text,mode:FULL,requestId:$id}){
          parseJob{id status} errors{... on AiSafetyError{code message}}
        }}
        """;
    private static ChatResponse Response(string text = "{}") => new(new ChatMessage(ChatRole.Assistant, text));

    [Fact]
    public async Task ReplayConflictAndUserIsolation()
    {
        var owner = await factory.GetOwnerClientAsync(); var guest = await factory.GetGuestClientAsync();
        factory.Llm.Respond = () => Response();
        var id = Guid.NewGuid(); var calls = factory.Llm.Calls;
        var first = (await owner.GraphQLAsync(Parse, new { text = "Posting", id })).GetProperty("parseListing");
        var replay = (await owner.GraphQLAsync(Parse, new { text = "Posting", id })).GetProperty("parseListing");
        Assert.Equal(first.GetProperty("parseJob").GetProperty("id").GetString(), replay.GetProperty("parseJob").GetProperty("id").GetString());
        Assert.Equal(calls + 1, factory.Llm.Calls);
        Assert.Equal("request-conflict", Code((await owner.GraphQLAsync(Parse, new { text = "Changed", id })).GetProperty("parseListing")));
        var other = (await guest.GraphQLAsync(Parse, new { text = "Posting", id })).GetProperty("parseListing");
        Assert.NotEqual(first.GetProperty("parseJob").GetProperty("id").GetString(), other.GetProperty("parseJob").GetProperty("id").GetString());
        Assert.Equal(calls + 2, factory.Llm.Calls);
        Assert.Equal("invalid-request-id", Code((await owner.GraphQLAsync(Parse, new { text = "Posting", id = Guid.Empty })).GetProperty("parseListing")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentDuplicatesAndOtherWorkNeverReachProvider(bool sendId)
    {
        var owner = await factory.GetOwnerClientAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        factory.Llm.RespondAsync = async ct => { entered.SetResult(); await release.Task.WaitAsync(ct); return Response(); };
        var before = factory.Llm.Calls;
        Guid? id = sendId ? Guid.NewGuid() : null;
        var first = owner.GraphQLAsync(Parse, new { text = "Concurrent", id });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            Assert.Equal("operation-in-progress", Code((await owner.GraphQLAsync(Parse, new { text = "Concurrent", id })).GetProperty("parseListing")));
            Assert.Equal("operation-in-progress", Code((await owner.GraphQLAsync(Parse, new { text = "Concurrent", id = Guid.NewGuid() })).GetProperty("parseListing")));
            Assert.Equal("ai-busy", Code((await owner.GraphQLAsync(Parse, new { text = "Different", id = Guid.NewGuid() })).GetProperty("parseListing")));
            Assert.Equal(before + 1, factory.Llm.Calls);
        }
        finally { release.TrySetResult(); await first; factory.Llm.RespondAsync = null; }
    }

    [Fact]
    public async Task EveryOtherAiMutationIsBlockedWhileOperationRuns()
    {
        var owner = await factory.GetOwnerClientAsync(); var listing = await BatchSafetyTests.Listing(owner);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        factory.Llm.RespondAsync = async ct => { entered.SetResult(); await release.Task.WaitAsync(ct); return Response(); };
        var pending = owner.GraphQLAsync(Parse, new { text = "Blocking", id = Guid.NewGuid() });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)); var fetches = factory.Pages.Requests; var calls = factory.Llm.Calls;
        try
        {
            foreach (var (name, input) in new[]
            {
                ("parseUrl", "url:\"https://example.com/other\""),
                ("parseListingFromUrl", $"listingId:{listing}"),
                ("parseProfileCv", "text:\"CV\""),
                ("analyzeTailoredCvSection", $"listingId:{listing},section:SKILLS"),
                ("analyzeAllTailoredCv", $"listingId:{listing}"),
            })
            {
                var payload = (await owner.GraphQLAsync($"mutation{{{name}(input:{{{input}}}){{errors{{... on AiSafetyError{{code}}}}}}}}"))
                    .GetProperty(name);
                Assert.Equal("ai-busy", Code(payload));
            }
            Assert.Equal(fetches, factory.Pages.Requests); Assert.Equal(calls, factory.Llm.Calls);
        }
        finally { release.TrySetResult(); await pending; factory.Llm.RespondAsync = null; }
    }

    [Fact]
    public async Task MissingHistoricalJobCannotCauseAnotherAttempt()
    {
        var owner = await factory.GetOwnerClientAsync(); factory.Llm.Respond = () => Response(); var id = Guid.NewGuid();
        await owner.GraphQLAsync(Parse, new { text = "Missing job", id }); var before = factory.Llm.Calls;
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var receipt = await db.AiOperations.IgnoreQueryFilters().SingleAsync(o => o.RequestId == id);
        var jobId = receipt.ParseJobId; receipt.ParseJobId = null; await db.SaveChangesAsync();
        await db.ParseJobs.IgnoreQueryFilters().Where(j => j.Id == jobId).ExecuteDeleteAsync();
        Assert.Equal("result-unavailable", Code((await owner.GraphQLAsync(Parse, new { text = "Missing job", id })).GetProperty("parseListing")));
        Assert.Equal(before, factory.Llm.Calls);
    }

    [Fact]
    public async Task UrlReplayAndConflictDoNotFetchAgain()
    {
        var owner = await factory.GetOwnerClientAsync(); factory.Llm.Respond = () => Response();
        factory.Pages.Respond = () => new(HttpStatusCode.OK) { Content = new StringContent("<html><body>" + new string('x', 300) + "</body></html>") };
        const string query = """
            mutation($url:String!,$id:UUID){parseUrl(input:{url:$url,requestId:$id}){
              parseJob{id status} errors{... on AiSafetyError{code}}
            }}
            """;
        var id = Guid.NewGuid(); var before = factory.Pages.Requests; var calls = factory.Llm.Calls;
        await owner.GraphQLAsync(query, new { url = "https://example.com/posting", id });
        await owner.GraphQLAsync(query, new { url = "https://example.com/posting", id });
        var conflict = (await owner.GraphQLAsync(query, new { url = "https://example.com/other", id })).GetProperty("parseUrl");
        Assert.Equal("request-conflict", Code(conflict));
        Assert.Equal(before + 1, factory.Pages.Requests); Assert.Equal(calls + 1, factory.Llm.Calls);
    }

    [Fact]
    public async Task CvReplayDoesNotRetainSensitivePreview()
    {
        var owner = await factory.GetOwnerClientAsync();
        factory.Llm.Respond = () => Response("""{"schemaVersion":1,"contentLanguage":"ENGLISH","basics":{"fullName":"Private candidate"}}""");
        const string query = """
            mutation($id:UUID){parseProfileCv(input:{text:"Private CV input",requestId:$id}){
              result{status} errors{... on AiSafetyError{code}}
            }}
            """;
        var id = Guid.NewGuid(); var before = factory.Llm.Calls;
        Assert.Equal("SUCCEEDED", (await owner.GraphQLAsync(query, new { id })).GetProperty("parseProfileCv").GetProperty("result").GetProperty("status").GetString());
        Assert.Equal("result-unavailable", Code((await owner.GraphQLAsync(query, new { id })).GetProperty("parseProfileCv")));
        Assert.Equal(before + 1, factory.Llm.Calls);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var receipt = await db.AiOperations.IgnoreQueryFilters().SingleAsync(o => o.RequestId == id);
        var stored = JsonSerializer.Serialize(receipt);
        Assert.DoesNotContain("Private", stored); Assert.Null(receipt.ParseJobId);
        Assert.NotNull(receipt.ProtectedWorkDigest);
    }

    [Fact]
    public async Task ReplayAfterDefaultChangeUsesOriginalJob()
    {
        var owner = await factory.GetOwnerClientAsync(); factory.Llm.Respond = () => Response();
        var id = Guid.NewGuid(); await owner.GraphQLAsync(Parse, new { text = "Original", id });
        var before = factory.Llm.Calls;
        await owner.GraphQLAsync("mutation{setDefaultLlmModel(input:{model:\"test-strong-model\"}){llmModels{defaultModel}}}");
        try { await owner.GraphQLAsync(Parse, new { text = "Original", id }); Assert.Equal(before, factory.Llm.Calls); }
        finally { await owner.GraphQLAsync("mutation{setDefaultLlmModel(input:{model:null}){llmModels{defaultModel}}}"); }
    }

    [Fact]
    public async Task AnonymousGuardedOperationsRemainPrivate()
    {
        using var anonymous = factory.CreateClient();
        var response = await anonymous.PostAsJsonAsync("/graphql", new { query = Parse, variables = new { text = "Posting" } });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    internal static string? Code(JsonElement payload) => payload.GetProperty("errors")[0].GetProperty("code").GetString();
}
