using System.Text.Json;
using JobHunt.Api.Data;
using JobHunt.Api.Features.Listings;
using JobHunt.Api.Features.Profiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace JobHunt.Api.Tests.Features.Profiles;

public class ProfileSummaryTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Suggest = """
        mutation { suggestProfileSummary(input: {}) { result { status summary error } errors { __typename } } }
        """;

    private static void Respond(ApiFactory factory, string json) =>
        factory.Llm.Respond = () => new ChatResponse(new ChatMessage(ChatRole.Assistant, json));

    private static async Task<JsonElement> SuggestAsync(HttpClient client) =>
        (await client.GraphQLAsync(Suggest)).GetProperty("suggestProfileSummary").GetProperty("result");

    private static async Task<string?> SummaryAsync(HttpClient client) =>
        (await client.GraphQLAsync("{ profile { summary } }")).GetProperty("profile").GetProperty("summary").GetString();

    [Fact]
    public async Task Suggest_SendsOwnProfile_ReturnsSummary_AndSavesNothing()
    {
        var client = await factory.GetGuestClientAsync();
        await client.GraphQLAsync("""
            mutation { updateProfile(input: { headline: "Vue developer", summary: "Old summary", hobbies: [], links: [] }) { profile { id } } }
            """);
        await ProfileEntryTests.AddExperienceAsync(client, employer: "Summary Employer");
        await ProfileEntryTests.AddExperienceAsync(await factory.GetSecondOwnerClientAsync(), employer: "Foreign Summary Employer");
        Respond(factory, """{"summary":"  Vue developer who ships.  "}""");

        var result = await SuggestAsync(client);

        Assert.Equal("SUCCEEDED", result.GetProperty("status").GetString());
        Assert.Equal("Vue developer who ships.", result.GetProperty("summary").GetString());
        Assert.Equal(ProfileSummaryPromptQueries.Instructions(Language.English, external: false), factory.Llm.LastMessages[0].Text);
        Assert.Contains("years alone are not seniority", factory.Llm.LastMessages[0].Text);
        Assert.DoesNotContain("at what level", factory.Llm.LastMessages[0].Text);
        Assert.Contains("Summary Employer", factory.Llm.LastMessages[1].Text);
        Assert.Contains("Old summary", factory.Llm.LastMessages[1].Text);
        Assert.DoesNotContain("Foreign Summary Employer", factory.Llm.LastMessages[1].Text);
        Assert.Equal("Old summary", await SummaryAsync(client));
        using var scope = factory.Services.CreateScope();
        var usage = await scope.ServiceProvider.GetRequiredService<AppDbContext>().LlmUsages
            .IgnoreQueryFilters().OrderByDescending(u => u.Id).FirstAsync();
        Assert.Equal(ProfileSummarySuggester.Operation, usage.Operation);
    }

    [Fact]
    public async Task Suggest_NotEnoughEvidence_SucceedsWithNull()
    {
        var client = await factory.GetGuestClientAsync();
        Respond(factory, """{"summary":null}""");

        var result = await SuggestAsync(client);

        Assert.Equal("SUCCEEDED", result.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("summary").ValueKind);
    }

    [Theory]
    [InlineData("no")]
    [InlineData("""{"summary":42}""")]
    public async Task Suggest_UnreadableAnswer_Fails(string answer)
    {
        var client = await factory.GetGuestClientAsync();
        Respond(factory, answer);

        var result = await SuggestAsync(client);

        Assert.Equal("FAILED", result.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("summary").ValueKind);
    }

    [Fact]
    public async Task Suggest_TooLong_Fails()
    {
        var client = await factory.GetGuestClientAsync();
        Respond(factory, JsonSerializer.Serialize(new { summary = new string('a', ProfileSummaryPromptQueries.MaxLength + 1) }));

        var result = await SuggestAsync(client);

        Assert.Equal("FAILED", result.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Prompt_IsExternal_InContentLanguage_WithProfile()
    {
        var client = await factory.GetGuestClientAsync();
        await ProfileEntryTests.AddExperienceAsync(client, employer: "Prompted Employer");

        var prompt = (await client.GraphQLAsync("{ profileSummaryPrompt { version instructions } }"))
            .GetProperty("profileSummaryPrompt");

        Assert.Equal(ProfileSummaryPromptQueries.Version, prompt.GetProperty("version").GetInt32());
        var text = prompt.GetProperty("instructions").GetString();
        Assert.StartsWith(ProfileSummaryPromptQueries.Instructions(Language.English, external: true), text);
        Assert.Contains("```json", text);
        Assert.Contains("Prompted Employer", text);
    }
}
