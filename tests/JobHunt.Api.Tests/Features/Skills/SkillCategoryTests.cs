using System.Text.Json;
using JobHunt.Api.Tests.Features.Applications;

namespace JobHunt.Api.Tests.Features.Skills;

public class SkillCategoryTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string SetCategory = """
        mutation ($skillId: Int!, $categoryId: Int) {
          setSkillCategory(input: { skillId: $skillId, categoryId: $categoryId }) {
            skill { id name category { id name } } errors { __typename }
          }
        }
        """;

    private static async Task<JsonElement> SetCategoryAsync(HttpClient client, int skillId, int? categoryId) =>
        (await client.GraphQLAsync(SetCategory, new { skillId, categoryId })).GetProperty("setSkillCategory");

    private static async Task<JsonElement> CreateCategoryAsync(HttpClient client, string name, string? description = null) =>
        (await client.GraphQLAsync("""
            mutation ($name: String!, $description: String) {
              createSkillCategory(input: { name: $name, description: $description }) {
                skillCategory { id name description sortOrder } errors { __typename }
              }
            }
            """, new { name, description })).GetProperty("createSkillCategory");

    private static async Task<int> CreateCategoryIdAsync(HttpClient client, string name) =>
        (await CreateCategoryAsync(client, name)).GetProperty("skillCategory").GetProperty("id").GetInt32();

    private static async Task<JsonElement[]> CategoriesAsync(HttpClient client) =>
        [.. (await client.GraphQLAsync("""
            { skillCategories(first: 50) { nodes { id name sortOrder skillCount translations { language name } } } }
            """)).GetProperty("skillCategories").GetProperty("nodes").EnumerateArray()];

    private static async Task<int> CategoryIdAsync(HttpClient client, string name) =>
        (await CategoriesAsync(client)).Single(c => c.GetProperty("name").GetString() == name).GetProperty("id").GetInt32();

    private static async Task<int> CreateSkillAsync(HttpClient client, string name) =>
        (await client.GraphQLAsync("""
            mutation ($name: String!) { setProfileSkill(input: { name: $name, level: 3 }) { profileSkill { skill { id } } } }
            """, new { name }))
        .GetProperty("setProfileSkill").GetProperty("profileSkill").GetProperty("skill").GetProperty("id").GetInt32();

    private static async Task<string?> CategoryOfAsync(HttpClient client, string name)
    {
        var category = (await client.GraphQLAsync("""
            query ($name: String!) { skills(search: $name) { nodes { name category { name } } } }
            """, new { name }))
            .GetProperty("skills").GetProperty("nodes").EnumerateArray()
            .Single(s => s.GetProperty("name").GetString() == name).GetProperty("category");
        return category.ValueKind == JsonValueKind.Null ? null : category.GetProperty("name").GetString();
    }

    private static async Task<int> CreateListingAsync(HttpClient client, string skill, string? categoryName) =>
        (await client.GraphQLAsync("""
            mutation ($listing: ListingInput!) { createListing(input: { listing: $listing }) { listing { id } } }
            """, new
        {
            listing = new
            {
                title = "Developer", companyName = "Skill category company", jobRoleName = "Developer",
                url = $"https://skill-category.example/{Guid.NewGuid()}", postedOn = "2026-10-01",
                deadlineType = "UNSPECIFIED", skills = new[] { new { name = skill, level = 3, isRequired = true, categoryName } },
                languages = Array.Empty<object>(), locations = Array.Empty<object>(),
            },
        })).GetProperty("createListing").GetProperty("listing").GetProperty("id").GetInt32();

    private static async Task<JsonElement> MoveAsync(HttpClient client, int id, int position) =>
        (await client.GraphQLAsync("""
            mutation ($id: Int!, $position: Int!) {
              moveSkillCategory(input: { id: $id, position: $position }) { skillCategories { id sortOrder } errors { __typename } }
            }
            """, new { id, position })).GetProperty("moveSkillCategory");

    private static async Task<JsonElement> DeleteAsync(HttpClient client, int id) =>
        (await client.GraphQLAsync("""
            mutation ($id: Int!) { deleteSkillCategory(input: { id: $id }) { id errors { __typename } } }
            """, new { id })).GetProperty("deleteSkillCategory");

    [Fact]
    public async Task NewUser_GetsStarterCategoriesInOrder_WithNorwegianNames()
    {
        var client = await factory.GetSecondOwnerClientAsync();

        var categories = await CategoriesAsync(client);

        Assert.Equal(["Languages", "Frameworks", "Databases", "Cloud & DevOps", "Testing", "AI & ML", "Tools", "Methods"],
            categories.Take(8).Select(c => c.GetProperty("name").GetString()));
        var languages = categories[0].GetProperty("translations").EnumerateArray().Single();
        Assert.Equal("NORWEGIAN", languages.GetProperty("language").GetString());
        Assert.Equal("Programmeringsspråk", languages.GetProperty("name").GetString());
    }

    [Fact]
    public async Task SetSkillCategory_SetsThenClears_AndCountsSkills()
    {
        var client = await factory.GetOwnerClientAsync();
        var category = await CreateCategoryIdAsync(client, "Counted category");
        var id = await CreateSkillAsync(client, "CategoryDocker");

        var set = await SetCategoryAsync(client, id, category);
        Assert.Equal("Counted category", set.GetProperty("skill").GetProperty("category").GetProperty("name").GetString());
        Assert.Equal(1, (await CategoriesAsync(client)).Single(c => c.GetProperty("id").GetInt32() == category)
            .GetProperty("skillCount").GetInt32());

        var cleared = await SetCategoryAsync(client, id, null);
        Assert.Equal(JsonValueKind.Null, cleared.GetProperty("skill").GetProperty("category").ValueKind);
    }

    [Fact]
    public async Task SetSkillCategory_OtherUsersSkillOrCategory_ReturnsNotFound()
    {
        var owner = await factory.GetOwnerClientAsync();
        var other = await factory.GetSecondOwnerClientAsync();
        var id = await CreateSkillAsync(owner, "CategoryIsolated");
        var othersCategory = await CreateCategoryIdAsync(other, "Other user's category");

        Assert.Equal("SkillNotFoundError", ApplicationTests.ErrorType(await SetCategoryAsync(other, id, othersCategory)));
        Assert.Equal("SkillNotFoundError", ApplicationTests.ErrorType(await SetCategoryAsync(owner, 999_999, null)));
        Assert.Equal("SkillCategoryNotFoundError", ApplicationTests.ErrorType(await SetCategoryAsync(owner, id, othersCategory)));
        Assert.Equal("SkillCategoryNotFoundError", ApplicationTests.ErrorType(await MoveAsync(owner, othersCategory, 0)));
        Assert.Equal("SkillCategoryNotFoundError", ApplicationTests.ErrorType(await DeleteAsync(owner, othersCategory)));
        Assert.Null(await CategoryOfAsync(owner, "CategoryIsolated"));
        Assert.DoesNotContain(await CategoriesAsync(owner), c => c.GetProperty("id").GetInt32() == othersCategory);
    }

    [Fact]
    public async Task CreateListing_CategoryNameFillsOnlyUncategorizedSkills_IgnoringUnknownNames()
    {
        var client = await factory.GetOwnerClientAsync();
        var id = await CreateSkillAsync(client, "CategoryPostgres");
        await SetCategoryAsync(client, id, await CategoryIdAsync(client, "Databases"));

        await CreateListingAsync(client, "CategoryPostgres", "Tools");
        await CreateListingAsync(client, "CategoryNewVue", "frameworks");
        await CreateListingAsync(client, "CategoryNewThing", "Invented category");

        Assert.Equal("Databases", await CategoryOfAsync(client, "CategoryPostgres"));
        Assert.Equal("Frameworks", await CategoryOfAsync(client, "CategoryNewVue"));
        Assert.Null(await CategoryOfAsync(client, "CategoryNewThing"));
    }

    [Fact]
    public async Task Create_AppendsLast_ReusesSameName_AndValidates()
    {
        var client = await factory.GetOwnerClientAsync();

        var created = (await CreateCategoryAsync(client, " Mobile ", "iOS and Android")).GetProperty("skillCategory");
        var again = (await CreateCategoryAsync(client, "MOBILE")).GetProperty("skillCategory");

        Assert.Equal("Mobile", created.GetProperty("name").GetString());
        Assert.Equal("iOS and Android", created.GetProperty("description").GetString());
        Assert.Equal(created.GetProperty("id").GetInt32(), again.GetProperty("id").GetInt32());
        Assert.Equal((await CategoriesAsync(client)).Max(c => c.GetProperty("sortOrder").GetInt32()),
            created.GetProperty("sortOrder").GetInt32());
        Assert.Equal("InvalidSkillCategoryError", ApplicationTests.ErrorType(await CreateCategoryAsync(client, " ")));
        Assert.Equal("InvalidSkillCategoryError", ApplicationTests.ErrorType(await CreateCategoryAsync(client, "Long", new string('x', 501))));
    }

    [Fact]
    public async Task Update_RenamesAndRejectsDuplicates()
    {
        var client = await factory.GetOwnerClientAsync();
        var id = await CreateCategoryIdAsync(client, "Rename me");
        const string Update = """
            mutation ($id: Int!, $name: String!) {
              updateSkillCategory(input: { id: $id, name: $name }) { skillCategory { name description } errors { __typename } }
            }
            """;

        var renamed = (await client.GraphQLAsync(Update, new { id, name = "Renamed" })).GetProperty("updateSkillCategory");
        var duplicate = (await client.GraphQLAsync(Update, new { id, name = "tools" })).GetProperty("updateSkillCategory");

        Assert.Equal("Renamed", renamed.GetProperty("skillCategory").GetProperty("name").GetString());
        Assert.Equal("DuplicateSkillCategoryError", ApplicationTests.ErrorType(duplicate));
    }

    [Fact]
    public async Task Move_RenumbersInNewOrder_AndRejectsOutOfRange()
    {
        var client = await factory.GetOwnerClientAsync();
        var id = await CreateCategoryIdAsync(client, "Move me first");

        var moved = await MoveAsync(client, id, 0);

        var ids = moved.GetProperty("skillCategories").EnumerateArray().Select(c => c.GetProperty("id").GetInt32()).ToList();
        Assert.Equal(id, ids[0]);
        Assert.Equal(ids, (await CategoriesAsync(client)).Select(c => c.GetProperty("id").GetInt32()));
        Assert.Equal("InvalidSkillCategoryError", ApplicationTests.ErrorType(await MoveAsync(client, id, ids.Count)));
    }

    [Fact]
    public async Task Translation_SetsUpsertsAndDeletes()
    {
        var client = await factory.GetOwnerClientAsync();
        var id = await CreateCategoryIdAsync(client, "Translated category");
        const string Set = """
            mutation ($id: Int!, $name: String!) {
              setSkillCategoryTranslation(input: { categoryId: $id, language: NORWEGIAN, name: $name }) {
                skillCategory { translations { name } } errors { __typename }
              }
            }
            """;

        await client.GraphQLAsync(Set, new { id, name = "Første" });
        var updated = (await client.GraphQLAsync(Set, new { id, name = "Oversatt" })).GetProperty("setSkillCategoryTranslation");
        var deleted = (await client.GraphQLAsync("""
            mutation ($id: Int!) {
              deleteSkillCategoryTranslation(input: { categoryId: $id, language: NORWEGIAN }) { skillCategory { translations { name } } }
            }
            """, new { id })).GetProperty("deleteSkillCategoryTranslation");

        Assert.Equal(["Oversatt"], updated.GetProperty("skillCategory").GetProperty("translations").EnumerateArray()
            .Select(t => t.GetProperty("name").GetString()));
        Assert.Empty(deleted.GetProperty("skillCategory").GetProperty("translations").EnumerateArray());
    }

    [Fact]
    public async Task Delete_UncategorizesSkillsAndClearsDraftCategory()
    {
        var client = await factory.GetOwnerClientAsync();
        var category = await CreateCategoryIdAsync(client, "Deleted category");
        var skill = await CreateSkillAsync(client, "CategoryDeletedSkill");
        await SetCategoryAsync(client, skill, category);
        var listing = await CreateListingAsync(client, "CategoryDeletedSkill", null);
        await client.GraphQLAsync("""
            mutation ($listingId: Int!, $content: TailoredCvContentInput!) {
              saveTailoredCvDraft(input: { listingId: $listingId, content: $content }) { errors { __typename } }
            }
            """, new
        {
            listingId = listing,
            content = new { skills = new[] { new { name = "CategoryDeletedSkill", level = 3, sourceSkillId = skill, categoryId = category } } },
        });

        var deleted = await DeleteAsync(client, category);

        Assert.Equal(category, deleted.GetProperty("id").GetInt32());
        Assert.Null(await CategoryOfAsync(client, "CategoryDeletedSkill"));
        var draftSkill = (await client.GraphQLAsync("""
            query ($listingId: Int!) { tailoredCvDraft(listingId: $listingId) { content { skills { categoryId } } } }
            """, new { listingId = listing })).GetProperty("tailoredCvDraft").GetProperty("content").GetProperty("skills")[0];
        Assert.Equal(JsonValueKind.Null, draftSkill.GetProperty("categoryId").ValueKind);
    }

    [Fact]
    public async Task Prompts_ListTheUsersCategories()
    {
        var client = await factory.GetOwnerClientAsync();
        await CreateCategoryAsync(client, "Prompted category", "things the AI should file here");
        var data = await client.GraphQLAsync("{ listingParsePrompt { instructions } profileCvParsePrompt { instructions } }");

        foreach (var prompt in new[] { "listingParsePrompt", "profileCvParsePrompt" })
        {
            var text = data.GetProperty(prompt).GetProperty("instructions").GetString()!;
            Assert.Contains("\"Cloud & DevOps\": hosting, CI/CD", text);
            Assert.Contains("\"Prompted category\": things the AI should file here", text);
            Assert.Contains("categoryName", text);
        }
    }
}
