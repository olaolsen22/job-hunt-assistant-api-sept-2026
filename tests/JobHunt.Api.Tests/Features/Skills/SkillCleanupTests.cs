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
            skills { id name category { name } } errors { __typename }
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
    public async Task Merge_MovesProfileSkillsTagsAndListings_KeepingTargetsOwnClaim()
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
        Assert.Equal(3, skill.GetProperty("level").GetInt32());
        var tags = profile.GetProperty("experiences").EnumerateArray()
            .Single(e => e.GetProperty("id").GetInt32() == experience.GetProperty("id").GetInt32()).GetProperty("skills");
        Assert.Equal(["CleanVue"], tags.EnumerateArray().Select(s => s.GetProperty("name").GetString()));
        var listingSkill = Assert.Single((await ListingSkillsAsync(client, listing)).EnumerateArray());
        Assert.Equal(2, listingSkill.GetProperty("level").GetInt32());
        Assert.Equal(JsonValueKind.Null, listingSkill.GetProperty("minYears").ValueKind);
        Assert.False(listingSkill.GetProperty("isRequired").GetBoolean());
    }

    [Fact]
    public async Task Merge_TargetWithoutRows_TakesStrongestMergedRow()
    {
        var client = await factory.GetOwnerClientAsync();
        var target = (await client.GraphQLAsync("""
            mutation { setProfileSkill(input: { name: "CleanReact", level: 1 }) { profileSkill { skill { id } } } }
            """)).GetProperty("setProfileSkill").GetProperty("profileSkill").GetProperty("skill").GetProperty("id").GetInt32();
        await client.GraphQLAsync("mutation ($id: Int!) { removeProfileSkill(input: { skillId: $id }) { skillId } }", new { id = target });
        var react = await SetProfileSkillAsync(client, "CleanReact.js", 2);
        var reactJs = await SetProfileSkillAsync(client, "Clean ReactJS", 4);
        var listing = await CreateListingAsync(client,
            new { name = "CleanReact.js", level = 2, isRequired = true, minYears = 5 },
            new { name = "Clean ReactJS", level = 4, isRequired = false, minYears = 2 });

        await CleanUpAsync(client, new { skillIds = new[] { target, react, reactJs }, names = new[] { "CleanReact" } });

        var skill = (await ProfileAsync(client)).GetProperty("skills").EnumerateArray()
            .Single(s => s.GetProperty("skill").GetProperty("name").GetString() == "CleanReact");
        Assert.Equal(4, skill.GetProperty("level").GetInt32());
        var listingSkill = Assert.Single((await ListingSkillsAsync(client, listing)).EnumerateArray());
        Assert.Equal("CleanReact", listingSkill.GetProperty("skill").GetProperty("name").GetString());
        Assert.Equal(4, listingSkill.GetProperty("level").GetInt32());
        Assert.Equal(2, listingSkill.GetProperty("minYears").GetInt32());
        Assert.False(listingSkill.GetProperty("isRequired").GetBoolean());
    }

    [Fact]
    public async Task DeleteSkill_RemovesUnusedSkill_RefusesOneInUse()
    {
        var client = await factory.GetOwnerClientAsync();
        var used = await SetProfileSkillAsync(client, "CleanDeleteUsed", 3);
        var unused = (await client.GraphQLAsync("""
            mutation { setProfileSkill(input: { name: "CleanDeleteUnused", level: 1 }) { profileSkill { skill { id } } } }
            """)).GetProperty("setProfileSkill").GetProperty("profileSkill").GetProperty("skill").GetProperty("id").GetInt32();
        await client.GraphQLAsync("mutation ($id: Int!) { removeProfileSkill(input: { skillId: $id }) { skillId } }", new { id = unused });
        const string Delete = "mutation ($id: Int!) { deleteSkill(input: { id: $id }) { id errors { __typename } } }";

        var deleted = (await client.GraphQLAsync(Delete, new { id = unused })).GetProperty("deleteSkill");
        var refused = (await client.GraphQLAsync(Delete, new { id = used })).GetProperty("deleteSkill");
        var other = (await (await factory.GetSecondOwnerClientAsync()).GraphQLAsync(Delete, new { id = used })).GetProperty("deleteSkill");

        Assert.Equal(unused, deleted.GetProperty("id").GetInt32());
        Assert.Empty(await SkillNamesAsync(client, "CleanDeleteUnused"));
        Assert.Equal("SkillInUseError", ApplicationTests.ErrorType(refused));
        Assert.Equal("SkillNotFoundError", ApplicationTests.ErrorType(other));
        Assert.Equal(["CleanDeleteUsed"], await SkillNamesAsync(client, "CleanDeleteUsed"));
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

    private static async Task<int> CreateCategoryAsync(HttpClient client, string name) =>
        (await client.GraphQLAsync("""
            mutation ($name: String!) { createSkillCategory(input: { name: $name }) { skillCategory { id } } }
            """, new { name })).GetProperty("createSkillCategory").GetProperty("skillCategory").GetProperty("id").GetInt32();

    [Fact]
    public async Task Suggest_KeepsCategoriesOnlyForUncategorizedSkills_WithKnownCategories()
    {
        var client = await factory.GetOwnerClientAsync();
        var categoryId = await CreateCategoryAsync(client, "SuggestCatFrameworks");
        await SetProfileSkillAsync(client, "SuggestCatVue", 3);
        var set = await SetProfileSkillAsync(client, "SuggestCatSet", 3);
        await client.GraphQLAsync("""
            mutation ($skillId: Int!, $categoryId: Int) { setSkillCategory(input: { skillId: $skillId, categoryId: $categoryId }) { skill { id } } }
            """, new { skillId = set, categoryId });
        factory.Llm.Respond = () => new ChatResponse(new ChatMessage(ChatRole.Assistant, """
            {"operations":[],"translations":[],
             "categories":[{"skillName":"SuggestCatVue","categoryName":"suggestcatframeworks"},
                           {"skillName":"SuggestCatSet","categoryName":"SuggestCatFrameworks"},
                           {"skillName":"SuggestCatVue","categoryName":"Invented"},
                           {"skillName":"SuggestCatMissing","categoryName":"SuggestCatFrameworks"}]}
            """));

        var result = (await client.GraphQLAsync("""
            mutation { suggestSkillCleanup(input: {}) { result { status categories { skillName categoryName } } } }
            """)).GetProperty("suggestSkillCleanup").GetProperty("result");

        var category = Assert.Single(result.GetProperty("categories").EnumerateArray());
        Assert.Equal("SuggestCatVue", category.GetProperty("skillName").GetString());
        Assert.Equal("SuggestCatFrameworks", category.GetProperty("categoryName").GetString());
        Assert.Contains("SuggestCatSet {SuggestCatFrameworks}", factory.Llm.LastMessages[1].Text);
        Assert.Contains("<categories>", factory.Llm.LastMessages[1].Text);
    }

    [Fact]
    public async Task CleanUp_SetsCategories_AndRejectsUnknownCategory()
    {
        var client = await factory.GetOwnerClientAsync();
        await CreateCategoryAsync(client, "CleanCatTools");
        await SetProfileSkillAsync(client, "CleanCatGit", 3);
        const string mutation = """
            mutation ($categories: [SkillCleanupCategoryInput!]) {
              cleanUpSkills(input: { operations: [], categories: $categories }) {
                skills { name category { name } } errors { __typename }
              }
            }
            """;

        var ok = (await client.GraphQLAsync(mutation, new { categories = new[] { new { skillName = "cleancatgit", categoryName = "CleanCatTools" } } }))
            .GetProperty("cleanUpSkills");
        var unknown = (await client.GraphQLAsync(mutation, new { categories = new[] { new { skillName = "CleanCatGit", categoryName = "Nope" } } }))
            .GetProperty("cleanUpSkills");

        Assert.Equal("CleanCatTools", Assert.Single(ok.GetProperty("skills").EnumerateArray()).GetProperty("category").GetProperty("name").GetString());
        Assert.Equal("InvalidSkillCleanupError", unknown.GetProperty("errors")[0].GetProperty("__typename").GetString());
    }

    private const string Remove = """
        mutation ($operations: [SkillCleanupOperationInput!]!, $ids: [Int!]) {
          cleanUpSkills(input: { operations: $operations, removeSkillIds: $ids }) {
            skills { id } errors { __typename }
          }
        }
        """;

    [Fact]
    public async Task CleanUp_RemovesSkillsInUse_Everywhere()
    {
        var client = await factory.GetOwnerClientAsync();
        var area = await SetProfileSkillAsync(client, "RemoveBackend Development", 4);
        await SetProfileSkillAsync(client, "RemoveKeepC#", 3);
        await ProfileEntryTests.AddExperienceAsync(client, skills: ["RemoveBackend Development", "RemoveKeepC#"]);
        var listing = await CreateListingAsync(client,
            new { name = "RemoveBackend Development", level = 4 }, new { name = "RemoveKeepC#", level = 3 });

        var result = (await client.GraphQLAsync(Remove, new { operations = Array.Empty<object>(), ids = new[] { area } }))
            .GetProperty("cleanUpSkills");

        Assert.Equal(JsonValueKind.Null, result.GetProperty("errors").ValueKind);
        Assert.Empty(await SkillNamesAsync(client, "RemoveBackend"));
        var profile = await ProfileAsync(client);
        Assert.DoesNotContain(profile.GetProperty("skills").EnumerateArray(),
            s => s.GetProperty("skill").GetProperty("name").GetString() == "RemoveBackend Development");
        Assert.Contains(profile.GetProperty("experiences").EnumerateArray(),
            e => e.GetProperty("skills").EnumerateArray().Select(s => s.GetProperty("name").GetString()).SequenceEqual(["RemoveKeepC#"]));
        var listingSkill = Assert.Single((await ListingSkillsAsync(client, listing)).EnumerateArray());
        Assert.Equal("RemoveKeepC#", listingSkill.GetProperty("skill").GetProperty("name").GetString());
    }

    [Fact]
    public async Task CleanUp_Removal_RejectsForeignIds_AndOverlapWithOperations()
    {
        var client = await factory.GetOwnerClientAsync();
        var mine = await SetProfileSkillAsync(client, "RemoveOverlap", 3);
        var foreign = await SetProfileSkillAsync(await factory.GetSecondOwnerClientAsync(), "RemoveOtherOwner", 3);

        var overlap = (await client.GraphQLAsync(Remove, new
        {
            operations = new[] { new { skillIds = new[] { mine }, names = new[] { "RemoveOverlapped" } } }, ids = new[] { mine },
        })).GetProperty("cleanUpSkills");
        var other = (await client.GraphQLAsync(Remove, new { operations = Array.Empty<object>(), ids = new[] { foreign } }))
            .GetProperty("cleanUpSkills");

        Assert.Equal("InvalidSkillCleanupError", overlap.GetProperty("errors")[0].GetProperty("__typename").GetString());
        Assert.Equal("SkillNotFoundError", other.GetProperty("errors")[0].GetProperty("__typename").GetString());
        Assert.Equal(["RemoveOverlap"], await SkillNamesAsync(client, "RemoveOverlap"));
        Assert.Equal(["RemoveOtherOwner"], await SkillNamesAsync(await factory.GetSecondOwnerClientAsync(), "RemoveOtherOwner"));
    }

    [Fact]
    public async Task Suggest_KeepsRemovalsOfOwnSkillsNotInOperations()
    {
        var client = await factory.GetOwnerClientAsync();
        var area = await SetProfileSkillAsync(client, "SuggestRemoveDebugging", 3);
        var merged = await SetProfileSkillAsync(client, "SuggestRemovevue", 3);
        var foreign = await SetProfileSkillAsync(await factory.GetSecondOwnerClientAsync(), "SuggestRemoveForeign", 3);
        factory.Llm.Respond = () => new ChatResponse(new ChatMessage(ChatRole.Assistant, $$"""
            {"operations":[{"skillIds":[{{merged}}],"names":["SuggestRemoveVue"]}],
             "removals":[{"skillId":{{area}},"reason":"Generic activity"},{"skillId":{{area}},"reason":"dup"},
                         {"skillId":{{merged}},"reason":"x"},{"skillId":{{foreign}},"reason":"x"},{"skillId":null}]}
            """));

        var result = (await client.GraphQLAsync("""
            mutation { suggestSkillCleanup(input: {}) { result { status removals { skillId reason } } } }
            """)).GetProperty("suggestSkillCleanup").GetProperty("result");

        var removal = Assert.Single(result.GetProperty("removals").EnumerateArray());
        Assert.Equal(area, removal.GetProperty("skillId").GetInt32());
        Assert.Equal("Generic activity", removal.GetProperty("reason").GetString());
        Assert.Equal(["SuggestRemoveDebugging"], await SkillNamesAsync(client, "SuggestRemoveDebugging"));
    }
}
