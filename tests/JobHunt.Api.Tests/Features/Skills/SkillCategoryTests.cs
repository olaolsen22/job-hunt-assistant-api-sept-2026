using System.Text.Json;
using JobHunt.Api.Tests.Features.Applications;

namespace JobHunt.Api.Tests.Features.Skills;

public class SkillCategoryTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string SetCategory = """
        mutation ($skillId: Int!, $category: SkillCategory) {
          setSkillCategory(input: { skillId: $skillId, category: $category }) {
            skill { id name category } errors { __typename }
          }
        }
        """;

    private static async Task<JsonElement> SetCategoryAsync(HttpClient client, int skillId, string? category) =>
        (await client.GraphQLAsync(SetCategory, new { skillId, category })).GetProperty("setSkillCategory");

    private static async Task<int> CreateSkillAsync(HttpClient client, string name) =>
        (await client.GraphQLAsync("""
            mutation ($name: String!) { setProfileSkill(input: { name: $name, level: 3 }) { profileSkill { skill { id } } } }
            """, new { name }))
        .GetProperty("setProfileSkill").GetProperty("profileSkill").GetProperty("skill").GetProperty("id").GetInt32();

    private static async Task<string?> CategoryOfAsync(HttpClient client, string name) =>
        (await client.GraphQLAsync("""
            query ($name: String!) { skills(search: $name) { nodes { name category } } }
            """, new { name }))
        .GetProperty("skills").GetProperty("nodes").EnumerateArray()
        .Single(s => s.GetProperty("name").GetString() == name).GetProperty("category").GetString();

    private static Task CreateListingAsync(HttpClient client, string skill, string? category) =>
        client.GraphQLAsync("""
            mutation ($listing: ListingInput!) { createListing(input: { listing: $listing }) { listing { id } } }
            """, new
        {
            listing = new
            {
                title = "Developer", companyName = "Skill category company", jobRoleName = "Developer",
                url = $"https://skill-category.example/{Guid.NewGuid()}", postedOn = "2026-10-01",
                deadlineType = "UNSPECIFIED", skills = new[] { new { name = skill, level = 3, isRequired = true, category } },
                languages = Array.Empty<object>(), locations = Array.Empty<object>(),
            },
        });

    [Fact]
    public async Task SetSkillCategory_SetsThenClears()
    {
        var client = await factory.GetOwnerClientAsync();
        var id = await CreateSkillAsync(client, "CategoryDocker");

        var set = await SetCategoryAsync(client, id, "CLOUD_DEVOPS");
        Assert.Equal("CLOUD_DEVOPS", set.GetProperty("skill").GetProperty("category").GetString());

        var cleared = await SetCategoryAsync(client, id, null);
        Assert.Equal(JsonValueKind.Null, cleared.GetProperty("skill").GetProperty("category").ValueKind);
    }

    [Fact]
    public async Task SetSkillCategory_UnknownOrOtherUsersSkill_ReturnsNotFound()
    {
        var owner = await factory.GetOwnerClientAsync();
        var other = await factory.GetSecondOwnerClientAsync();
        var id = await CreateSkillAsync(owner, "CategoryIsolated");

        Assert.Equal("SkillNotFoundError", ApplicationTests.ErrorType(await SetCategoryAsync(other, id, "TOOL")));
        Assert.Equal("SkillNotFoundError", ApplicationTests.ErrorType(await SetCategoryAsync(owner, 999_999, "TOOL")));
        Assert.Null(await CategoryOfAsync(owner, "CategoryIsolated"));
    }

    [Fact]
    public async Task CreateListing_CategoryFillsOnlyUncategorizedSkills()
    {
        var client = await factory.GetOwnerClientAsync();
        var id = await CreateSkillAsync(client, "CategoryPostgres");
        await SetCategoryAsync(client, id, "DATABASE");

        await CreateListingAsync(client, "CategoryPostgres", "TOOL");
        await CreateListingAsync(client, "CategoryNewVue", "FRAMEWORK");

        Assert.Equal("DATABASE", await CategoryOfAsync(client, "CategoryPostgres"));
        Assert.Equal("FRAMEWORK", await CategoryOfAsync(client, "CategoryNewVue"));
    }

    [Fact]
    public async Task Prompts_ListEveryCategory()
    {
        var client = await factory.GetOwnerClientAsync();
        var data = await client.GraphQLAsync("{ listingParsePrompt { instructions } profileCvParsePrompt { instructions } }");

        foreach (var prompt in new[] { "listingParsePrompt", "profileCvParsePrompt" })
        {
            var text = data.GetProperty(prompt).GetProperty("instructions").GetString()!;
            foreach (var value in new[] { "LANGUAGE", "FRAMEWORK", "DATABASE", "CLOUD_DEVOPS", "TOOL", "METHOD" })
                Assert.Contains($"  {value}: ", text);
        }
    }
}
