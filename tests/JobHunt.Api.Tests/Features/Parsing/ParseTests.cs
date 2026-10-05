using Google.GenAI;
using JobHunt.Api.Data;
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
