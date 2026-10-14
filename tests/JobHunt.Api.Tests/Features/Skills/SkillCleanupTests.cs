using System.Text.Json;
using JobHunt.Api.Data;
using JobHunt.Api.Features.Skills;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using JobHunt.Api.Tests.Features.Applications;
using JobHunt.Api.Tests.Features.Profiles;

namespace JobHunt.Api.Tests.Features.Skills;

public class SkillCleanupTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string CleanUp = """
        mutation ($operations: [SkillCleanupOperationInput!]!) {
          cleanUpSkills(input: { operations: $operations }) {
            skills { id name category } errors { __typename }
          }
        }
        """;

    private static async Task<JsonElement> CleanUpAsync(HttpClient client, params object[] operations) =>
        (await client.GraphQLAsync(CleanUp, new { operations })).GetProperty("cleanUpSkills");

    private static async Task<int> SetProfileSkillAsync(HttpClient client, string name, int level) =>
        (await client.GraphQLAsync("""
            mutation ($name: String!, $level: Int!) {
              setProfileSkill(input: { name: $name, level: $level }) { profileSkill { skill { id } } }
            }
            """, new { name, level }))
        .GetProperty("setProfileSkill").GetProperty("profileSkill").GetProperty("skill").GetProperty("id").GetInt32();

    private static async Task<JsonElement> ProfileAsync(HttpClient client) =>
        (await client.GraphQLAsync("""
            { profile { skills { level skill { name } } experiences { id skills { name } } } }
            """)).GetProperty("profile");

    private static async Task<string[]> SkillNamesAsync(HttpClient client, string search) =>
        [.. (await client.GraphQLAsync("""
            query ($search: String!) { skills(search: $search) { nodes { name } } }
            """, new { search }))
        .GetProperty("skills").GetProperty("nodes").EnumerateArray().Select(s => s.GetProperty("name").GetString()!)];

    private static async Task<int> CreateListingAsync(HttpClient client, params object[] skills) =>
        (await client.GraphQLAsync("""
            mutation ($listing: ListingInput!) { createListing(input: { listing: $listing }) { listing { id } } }
            """, new
        {
            listing = new
            {
                title = "Developer", companyName = "Skill cleanup company", jobRoleName = "Developer",
                url = $"https://skill-cleanup.example/{Guid.NewGuid()}", postedOn = "2026-10-01",
                deadlineType = "UNSPECIFIED", skills,
                languages = Array.Empty<object>(), locations = Array.Empty<object>(),
            },
        })).GetProperty("createListing").GetProperty("listing").GetProperty("id").GetInt32();

    private static async Task<JsonElement> ListingSkillsAsync(HttpClient client, int id) =>
        (await client.GraphQLAsync("""
            query ($id: Int!) { listings(where: { id: { eq: $id } }) { items { skills { level minYears isRequired skill { name } } } } }
            """, new { id }))
        .GetProperty("listings").GetProperty("items")[0].GetProperty("skills");

    [Fact]
    public async Task Merge_MovesProfileSkillsTagsAndListings_KeepingStrongestClaim()
    {
        var client = await factory.GetOwnerClientAsync();
        var vue = await SetProfileSkillAsync(client, "CleanVue", 3);
        var vueJs = await SetProfileSkillAsync(client, "CleanVue.js", 5);
        var experience = await ProfileEntryTests.AddExperienceAsync(client, skills: ["CleanVue", "CleanVue.js"]);
        var listing = await CreateListingAsync(client,
            new { name = "CleanVue", level = 2, isRequired = false },
            new { name = "CleanVue.js", level = 4, isRequired = true, minYears = 3 });

        var result = await CleanUpAsync(client, new { skillIds = new[] { vue, vueJs }, names = new[] { "CleanVue" } });

        Assert.Equal([vue], result.GetProperty("skills").EnumerateArray().Select(s => s.GetProperty("id").GetInt32()));
        Assert.Equal(["CleanVue"], await SkillNamesAsync(client, "CleanVue"));
        var profile = await ProfileAsync(client);
        var skill = profile.GetProperty("skills").EnumerateArray()
            .Single(s => s.GetProperty("skill").GetProperty("name").GetString() == "CleanVue");
        Assert.Equal(5, skill.GetProperty("level").GetInt32());
        var tags = profile.GetProperty("experiences").EnumerateArray()
            .Single(e => e.GetProperty("id").GetInt32() == experience.GetProperty("id").GetInt32()).GetProperty("skills");
        Assert.Equal(["CleanVue"], tags.EnumerateArray().Select(s => s.GetProperty("name").GetString()));
        var listingSkill = Assert.Single((await ListingSkillsAsync(client, listing)).EnumerateArray());
        Assert.Equal(4, listingSkill.GetProperty("level").GetInt32());
        Assert.Equal(3, listingSkill.GetProperty("minYears").GetInt32());
        Assert.True(listingSkill.GetProperty("isRequired").GetBoolean());
    }

    [Fact]
    public async Task Split_ReplacesCompoundWithExistingAndNewSkills()
    {
        var client = await factory.GetOwnerClientAsync();
        await SetProfileSkillAsync(client, "SplitC#", 5);
        var compound = await SetProfileSkillAsync(client, "SplitC#/SplitNET", 4);
        await ProfileEntryTests.AddExperienceAsync(client, skills: ["SplitC#/SplitNET"]);

        var result = await CleanUpAsync(client, new { skillIds = new[] { compound }, names = new[] { "SplitC#", "SplitNET" } });

        Assert.Equal(["SplitC#", "SplitNET"], result.GetProperty("skills").EnumerateArray().Select(s => s.GetProperty("name").GetString()));
        Assert.Equal(["SplitC#", "SplitNET"], await SkillNamesAsync(client, "Split"));
        var levels = (await ProfileAsync(client)).GetProperty("skills").EnumerateArray()
            .Where(s => s.GetProperty("skill").GetProperty("name").GetString()!.StartsWith("Split"))
            .ToDictionary(s => s.GetProperty("skill").GetProperty("name").GetString()!, s => s.GetProperty("level").GetInt32());
        Assert.Equal(5, levels["SplitC#"]);
        Assert.Equal(4, levels["SplitNET"]);
    }

    [Fact]
    public async Task Rename_FixesCasingInPlace()
    {
        var client = await factory.GetOwnerClientAsync();
        var id = await SetProfileSkillAsync(client, "renametypescript", 3);

        var result = await CleanUpAsync(client, new { skillIds = new[] { id }, names = new[] { "RenameTypeScript" } });

        Assert.Equal(id, result.GetProperty("skills")[0].GetProperty("id").GetInt32());
        Assert.Equal(["RenameTypeScript"], await SkillNamesAsync(client, "renametypescript"));
    }

    [Fact]
    public async Task OtherUsersSkill_ReturnsNotFound_AndChangesNothing()
    {
        var owner = await factory.GetOwnerClientAsync();
        var other = await factory.GetSecondOwnerClientAsync();
        var mine = await SetProfileSkillAsync(other, "IsolatedMine", 3);
        var theirs = await SetProfileSkillAsync(owner, "IsolatedTheirs", 3);

        var result = await CleanUpAsync(other, new { skillIds = new[] { mine, theirs }, names = new[] { "IsolatedMine" } });

        Assert.Equal("SkillNotFoundError", ApplicationTests.ErrorType(result));
        Assert.Equal(["IsolatedTheirs"], await SkillNamesAsync(owner, "IsolatedTheirs"));
    }

    [Fact]
    public async Task InvalidPlan_ReturnsError_AndChangesNothing()
    {
        var client = await factory.GetOwnerClientAsync();
        var a = await SetProfileSkillAsync(client, "InvalidA", 3);
        var b = await SetProfileSkillAsync(client, "InvalidB", 3);

        var repeated = await CleanUpAsync(client,
            new { skillIds = new[] { a }, names = new[] { "InvalidC" } },
            new { skillIds = new[] { a, b }, names = new[] { "InvalidD" } });
        var blank = await CleanUpAsync(client, new { skillIds = new[] { a }, names = new[] { " " } });

        Assert.Equal("InvalidSkillCleanupError", ApplicationTests.ErrorType(repeated));
        Assert.Equal("InvalidSkillCleanupError", ApplicationTests.ErrorType(blank));
        Assert.Equal(["InvalidA", "InvalidB"], await SkillNamesAsync(client, "Invalid"));
    }

    [Fact]
    public async Task Prompt_ListsOwnSkillsWithIds()
    {
        var client = await factory.GetOwnerClientAsync();
        var id = await SetProfileSkillAsync(client, "PromptNuxt.js", 3);

        var prompt = (await client.GraphQLAsync("{ skillCleanupPrompt { instructions } }"))
            .GetProperty("skillCleanupPrompt").GetProperty("instructions").GetString();

        Assert.Contains($"{id}: PromptNuxt.js", prompt);
    }

    private const string Suggest = """
        mutation { suggestSkillCleanup(input: {}) { result { status error operations { skillIds names } } errors { __typename } } }
        """;

    [Fact]
    public async Task Suggest_UsesSharedPrompt_DropsUnknownIds_AndSavesOnlyUsage()
    {
        var client = await factory.GetOwnerClientAsync();
        var a = await SetProfileSkillAsync(client, "SuggestNuxt", 3);
        var b = await SetProfileSkillAsync(client, "SuggestNuxt.js", 3);
        var foreign = await SetProfileSkillAsync(await factory.GetSecondOwnerClientAsync(), "SuggestForeign", 3);
        factory.Llm.Respond = () => new ChatResponse(new ChatMessage(ChatRole.Assistant, $$"""
            {"operations":[{"skillIds":[{{a}},{{b}},{{foreign}}],"names":["SuggestNuxt"]},
                           {"skillIds":[{{foreign}}],"names":["X"]},
                           {"skillIds":[{{a}}],"names":["SuggestNuxt"]}]}
            """));

        var result = (await client.GraphQLAsync(Suggest)).GetProperty("suggestSkillCleanup").GetProperty("result");

        Assert.Equal("SUCCEEDED", result.GetProperty("status").GetString());
        var operation = Assert.Single(result.GetProperty("operations").EnumerateArray());
        Assert.Equal([a, b], operation.GetProperty("skillIds").EnumerateArray().Select(i => i.GetInt32()));
        Assert.Equal(SkillCleanupPromptQueries.Instructions, factory.Llm.LastMessages[0].Text);
        Assert.Contains($"{b}: SuggestNuxt.js", factory.Llm.LastMessages[1].Text);
        Assert.DoesNotContain("SuggestForeign", factory.Llm.LastMessages[1].Text);
        Assert.Equal(["SuggestNuxt", "SuggestNuxt.js"], await SkillNamesAsync(client, "SuggestNuxt"));
        using var scope = factory.Services.CreateScope();
        var usage = await scope.ServiceProvider.GetRequiredService<AppDbContext>().LlmUsages
            .IgnoreQueryFilters().OrderByDescending(u => u.Id).FirstAsync();
        Assert.True(usage.Success);
        Assert.Equal(SkillCleanupSuggester.Operation, usage.Operation);
    }

    [Fact]
    public async Task Suggest_UnreadableAnswer_Fails()
    {
        var client = await factory.GetOwnerClientAsync();
        factory.Llm.Respond = () => new ChatResponse(new ChatMessage(ChatRole.Assistant, "not json"));

        var result = (await client.GraphQLAsync(Suggest)).GetProperty("suggestSkillCleanup").GetProperty("result");

        Assert.Equal("FAILED", result.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("operations").ValueKind);
    }
}
