using System.Net;
using Google.GenAI;
using JobHunt.Api.Data;
using JobHunt.Api.Tests.Features.Listings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace JobHunt.Api.Tests.Features.Parsing;

public class ParseTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string ParseListing = """
        mutation ($text: String!, $mode: ParseMode!) {
          parseListing(input: { text: $text, mode: $mode }) {
            parseJob { id mode status result error }
            errors { __typename }
          }
        }
        """;

    private static ChatResponse Answer(string text) =>
        new(new ChatMessage(ChatRole.Assistant, text)) { Usage = new UsageDetails { InputTokenCount = 1200, OutputTokenCount = 300 } };

    [Fact]
    public async Task ParseListing_Full_Succeeds_AndLogsUsage()
    {
        var client = await factory.GetOwnerClientAsync();
        factory.Llm.Respond = () => Answer("""{ "title": "Backend-utvikler" }""");

        var job = (await client.GraphQLAsync(ParseListing, new { text = "  Vi søker en backend-utvikler  ", mode = "FULL" }))
            .GetProperty("parseListing").GetProperty("parseJob");

        Assert.Equal("SUCCEEDED", job.GetProperty("status").GetString());
        Assert.Equal("""{ "title": "Backend-utvikler" }""", job.GetProperty("result").GetString());
        Assert.Equal("<posting>\nVi søker en backend-utvikler\n</posting>", factory.Llm.LastMessages[1].Text);
        Assert.Contains("ONE JSON object", factory.Llm.LastMessages[0].Text);

        // Tests in a class run one at a time, so the newest row is this call's. No request here, so no user filter.
        using var scope = factory.Services.CreateScope();
        var usage = await scope.ServiceProvider.GetRequiredService<AppDbContext>().LlmUsages
            .IgnoreQueryFilters().OrderByDescending(u => u.Id).FirstAsync();
        Assert.True(usage.Success);
        Assert.Equal(1200, usage.InputTokens);
        Assert.Equal(300, usage.OutputTokens);
    }

    [Theory]
    [InlineData("FULL")]
    [InlineData("QUICK")]
    [InlineData("URL")]
    [InlineData("SAVED_URL")]
    public async Task CategorySuggestions_RemainResults_WithoutChangingRoles(string mode)
    {
        var client = await factory.GetOwnerClientAsync();
        var listingId = mode == "SAVED_URL"
            ? await ListingTests.ListingWithStatusAsync(client, ListingTests.UniqueUrl(), "SAVED") : 0;
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var categories = await db.JobRoleCategories.IgnoreQueryFilters().OrderBy(c => c.Id)
            .Select(c => new { c.Id, c.Name }).ToListAsync();
        var roles = await db.JobRoles.IgnoreQueryFilters().OrderBy(r => r.Id)
            .Select(r => new { r.Id, r.CategoryId }).ToListAsync();
        var result = mode == "QUICK" ? """[{"jobRoleName":"New technical role","jobRoleCategoryName":"IT"}]"""
            : """{"jobRoleName":"New technical role","jobRoleCategoryName":"IT"}""";
        factory.Llm.Respond = () => Answer(result);
        factory.Pages.Respond = () => Page(AdPage);
        var job = mode switch
        {
            "URL" => (await client.GraphQLAsync(ParseUrl, new { url = ListingTests.UniqueUrl() })).GetProperty("parseUrl").GetProperty("parseJob"),
            "SAVED_URL" => (await client.GraphQLAsync(ParseListingFromUrl, new { listingId })).GetProperty("parseListingFromUrl").GetProperty("parseJob"),
            _ => (await client.GraphQLAsync(ParseListing, new { text = "An ad", mode })).GetProperty("parseListing").GetProperty("parseJob"),
        };
        Assert.Equal("SUCCEEDED", job.GetProperty("status").GetString());
        Assert.Equal(result, job.GetProperty("result").GetString());
        Assert.Contains("jobRoleCategoryName", factory.Llm.LastMessages[0].Text);
        Assert.Equal(categories, await db.JobRoleCategories.IgnoreQueryFilters().OrderBy(c => c.Id).Select(c => new { c.Id, c.Name }).ToListAsync());
        Assert.Equal(roles, await db.JobRoles.IgnoreQueryFilters().OrderBy(r => r.Id).Select(r => new { r.Id, r.CategoryId }).ToListAsync());
    }

    [Theory]
    [InlineData("FULL", true)]
    [InlineData("QUICK", false)]
    public async Task ParseListing_AsksForThinking_OnlyInFullMode(string mode, bool thinking)
    {
        var client = await factory.GetOwnerClientAsync();
        factory.Llm.Respond = () => Answer(mode == "FULL" ? "{}" : "[]");

        await client.GraphQLAsync(ParseListing, new { text = "Some ad", mode });

        Assert.Equal(thinking ? ReasoningEffort.Low : null, factory.Llm.LastOptions!.Reasoning?.Effort);
    }

    [Fact]
    public async Task ParseListing_CountsThinkingTokensAsOutput()
    {
        var client = await factory.GetOwnerClientAsync();
        factory.Llm.Respond = () => new ChatResponse(new ChatMessage(ChatRole.Assistant, "{}"))
        {
            Usage = new UsageDetails { InputTokenCount = 100, OutputTokenCount = 50, ReasoningTokenCount = 400 },
        };

        await client.GraphQLAsync(ParseListing, new { text = "Some ad", mode = "FULL" });

        using var scope = factory.Services.CreateScope();
        var usage = await scope.ServiceProvider.GetRequiredService<AppDbContext>().LlmUsages
            .IgnoreQueryFilters().OrderByDescending(u => u.Id).FirstAsync();
        Assert.Equal(450, usage.OutputTokens);
    }

    [Fact]
    public async Task ParseListing_QuickGettingObject_Fails()
    {
        var client = await factory.GetOwnerClientAsync();
        factory.Llm.Respond = () => Answer("""{ "title": "Not an array" }""");

        var job = (await client.GraphQLAsync(ParseListing, new { text = "Three ads", mode = "QUICK" }))
            .GetProperty("parseListing").GetProperty("parseJob");

        Assert.Equal("FAILED", job.GetProperty("status").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, job.GetProperty("result").ValueKind);
        Assert.Contains("ONE JSON array", factory.Llm.LastMessages[0].Text);
    }

    [Fact]
    public async Task ParseListing_RateLimited_FailsWithReadableError()
    {
        var client = await factory.GetOwnerClientAsync();
        factory.Llm.Respond = () => throw new ClientError("Quota exceeded", 429, "RESOURCE_EXHAUSTED", null);

        var job = (await client.GraphQLAsync(ParseListing, new { text = "An ad", mode = "FULL" }))
            .GetProperty("parseListing").GetProperty("parseJob");

        Assert.Equal("FAILED", job.GetProperty("status").GetString());
        Assert.Contains("free-tier limit", job.GetProperty("error").GetString());
    }

    [Fact]
    public async Task ParseListing_Timeout_FailsJob_AndLogsUsage()
    {
        var client = await factory.GetOwnerClientAsync();
        factory.Llm.Respond = () => throw new TaskCanceledException("timed out", new TimeoutException());

        var job = (await client.GraphQLAsync(ParseListing, new { text = "An ad", mode = "FULL" }))
            .GetProperty("parseListing").GetProperty("parseJob");

        Assert.Equal("FAILED", job.GetProperty("status").GetString());
        Assert.Contains("took too long", job.GetProperty("error").GetString());

        const string query = "query ($id: UUID!) { parseJob(id: $id) { status } }";
        var saved = (await client.GraphQLAsync(query, new { id = job.GetProperty("id").GetString() })).GetProperty("parseJob");
        Assert.Equal("FAILED", saved.GetProperty("status").GetString());

        using var scope = factory.Services.CreateScope();
        var usage = await scope.ServiceProvider.GetRequiredService<AppDbContext>().LlmUsages
            .IgnoreQueryFilters().OrderByDescending(u => u.Id).FirstAsync();
        Assert.False(usage.Success);
        Assert.Equal("timeout", usage.ErrorCode);
    }

    [Fact]
    public async Task ParseListing_BlankText_ReturnsInvalidParseRequestError()
    {
        var client = await factory.GetOwnerClientAsync();

        var payload = (await client.GraphQLAsync(ParseListing, new { text = "   ", mode = "FULL" })).GetProperty("parseListing");

        Assert.Equal("InvalidParseRequestError", payload.GetProperty("errors")[0].GetProperty("__typename").GetString());
    }

    private const string ParseListingFromUrl = """
        mutation ($listingId: Int!) {
          parseListingFromUrl(input: { listingId: $listingId }) {
            parseJob { id mode status result }
            errors { __typename }
          }
        }
        """;

    private const string AdPage = """
        <html><head><script type="application/ld+json">{"@type":"JobPosting","title":"Backend Engineer",
        "hiringOrganization":{"name":"Six Robotics"},"description":"<p>Build things.</p>"}</script></head>
        <body><article>Similar job: Other</article></body></html>
        """;

    private static HttpResponseMessage Page(string html, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(html) };

    private async Task<(int Jobs, int Usages)> CountsAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return (await db.ParseJobs.IgnoreQueryFilters().CountAsync(), await db.LlmUsages.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task ParseListingFromUrl_Succeeds_AndSendsTheExtractedPosting()
    {
        var client = await factory.GetOwnerClientAsync();
        var listingId = await ListingTests.ListingWithStatusAsync(client, ListingTests.UniqueUrl(), "SAVED");
        factory.Pages.Respond = () => Page(AdPage);
        factory.Llm.Respond = () => Answer("""{ "title": "Backend Engineer" }""");
        var before = await CountsAsync();

        var job = (await client.GraphQLAsync(ParseListingFromUrl, new { listingId }))
            .GetProperty("parseListingFromUrl").GetProperty("parseJob");

        Assert.Equal("SUCCEEDED", job.GetProperty("status").GetString());
        Assert.Equal("FULL", job.GetProperty("mode").GetString());
        var posting = factory.Llm.LastMessages[1].Text;
        Assert.Contains("Title: Backend Engineer", posting);
        Assert.Contains("Company: Six Robotics", posting);
        Assert.DoesNotContain("Similar job", posting);
        Assert.Equal((before.Jobs + 1, before.Usages + 1), await CountsAsync());
    }

    [Fact]
    public async Task ParseListingFromUrl_UnknownOrForeignListing_ReturnsNotFound_WithoutFetching()
    {
        var owner = await factory.GetOwnerClientAsync();
        var guest = await factory.GetGuestClientAsync();
        var guestListingId = await ListingTests.ListingWithStatusAsync(guest, ListingTests.UniqueUrl(), "SAVED");
        var requests = factory.Pages.Requests;
        var before = await CountsAsync();

        foreach (var listingId in new[] { guestListingId, 999_999 })
        {
            var errors = (await owner.GraphQLAsync(ParseListingFromUrl, new { listingId }))
                .GetProperty("parseListingFromUrl").GetProperty("errors");
            Assert.Equal("ListingNotFoundError", errors[0].GetProperty("__typename").GetString());
        }

        Assert.Equal(requests, factory.Pages.Requests);
        Assert.Equal(before, await CountsAsync());
    }

    [Theory]
    [InlineData(403, "denied")]
    [InlineData(200, "<html><body><div id=\"app\"></div></body></html>")]
    public async Task ParseListingFromUrl_UnreadablePage_ReturnsError_AndCreatesNothing(int status, string html)
    {
        var client = await factory.GetOwnerClientAsync();
        var listingId = await ListingTests.ListingWithStatusAsync(client, ListingTests.UniqueUrl(), "SAVED");
        factory.Pages.Respond = () => Page(html, (HttpStatusCode)status);
        var before = await CountsAsync();

        var errors = (await client.GraphQLAsync(ParseListingFromUrl, new { listingId }))
            .GetProperty("parseListingFromUrl").GetProperty("errors");

        Assert.Equal("InvalidParseRequestError", errors[0].GetProperty("__typename").GetString());
        Assert.Equal(before, await CountsAsync());
    }

    private const string ParseUrl = """
        mutation ($url: String!) {
          parseUrl(input: { url: $url }) {
            parseJob { id mode status result }
            errors { __typename }
          }
        }
        """;

    [Fact]
    public async Task ParseUrl_Succeeds_AndSendsTheExtractedPosting()
    {
        var client = await factory.GetOwnerClientAsync();
        factory.Pages.Respond = () => Page(AdPage);
        factory.Llm.Respond = () => Answer("""{ "title": "Backend Engineer" }""");
        var before = await CountsAsync();

        var job = (await client.GraphQLAsync(ParseUrl, new { url = ListingTests.UniqueUrl() }))
            .GetProperty("parseUrl").GetProperty("parseJob");

        Assert.Equal("SUCCEEDED", job.GetProperty("status").GetString());
        Assert.Equal("FULL", job.GetProperty("mode").GetString());
        Assert.Contains("Title: Backend Engineer", factory.Llm.LastMessages[1].Text);
        Assert.Equal((before.Jobs + 1, before.Usages + 1), await CountsAsync());
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("/jobs/1")]
    [InlineData("ftp://example.com/ad")]
    [InlineData("TOO_LONG")]
    public async Task ParseUrl_InvalidUrl_ReturnsError_WithoutFetching(string url)
    {
        if (url == "TOO_LONG") url = "https://example.com/" + new string('a', 2048);
        var client = await factory.GetOwnerClientAsync();
        var requests = factory.Pages.Requests;
        var before = await CountsAsync();

        var errors = (await client.GraphQLAsync(ParseUrl, new { url }))
            .GetProperty("parseUrl").GetProperty("errors");

        Assert.Equal("InvalidParseRequestError", errors[0].GetProperty("__typename").GetString());
        Assert.Equal(requests, factory.Pages.Requests);
        Assert.Equal(before, await CountsAsync());
    }

    [Theory]
    [InlineData(403, "denied")]
    [InlineData(200, "<html><body><div id=\"app\"></div></body></html>")]
    public async Task ParseUrl_UnreadablePage_ReturnsError_AndCreatesNothing(int status, string html)
    {
        var client = await factory.GetOwnerClientAsync();
        factory.Pages.Respond = () => Page(html, (HttpStatusCode)status);
        var before = await CountsAsync();

        var errors = (await client.GraphQLAsync(ParseUrl, new { url = ListingTests.UniqueUrl() }))
            .GetProperty("parseUrl").GetProperty("errors");

        Assert.Equal("InvalidParseRequestError", errors[0].GetProperty("__typename").GetString());
        Assert.Equal(before, await CountsAsync());
    }

    [Fact]
    public async Task ParseJob_IsOnlyVisibleToItsOwner()
    {
        var owner = await factory.GetOwnerClientAsync();
        var guest = await factory.GetGuestClientAsync();
        factory.Llm.Respond = () => Answer("{}");
        var id = (await owner.GraphQLAsync(ParseListing, new { text = "An ad", mode = "FULL" }))
            .GetProperty("parseListing").GetProperty("parseJob").GetProperty("id").GetString();

        const string query = "query ($id: UUID!) { parseJob(id: $id) { id status } }";
        var own = (await owner.GraphQLAsync(query, new { id })).GetProperty("parseJob");
        var other = (await guest.GraphQLAsync(query, new { id })).GetProperty("parseJob");

        Assert.Equal("SUCCEEDED", own.GetProperty("status").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, other.ValueKind);
    }
}
