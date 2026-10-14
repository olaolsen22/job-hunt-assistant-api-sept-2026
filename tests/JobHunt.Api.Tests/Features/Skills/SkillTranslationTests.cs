using System.Text.Json;
using JobHunt.Api.Tests.Features.Applications;
using Microsoft.Extensions.AI;

namespace JobHunt.Api.Tests.Features.Skills;

public class SkillTranslationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Set = """
        mutation ($skillId: Int!, $name: String!) {
          setSkillTranslation(input: { skillId: $skillId, language: NORWEGIAN, name: $name }) {
            skill { translations { language name } } errors { __typename }
          }
        }
        """;

    private static async Task<int> SkillAsync(HttpClient client, string name, int level = 3) =>
        (await client.GraphQLAsync("""
            mutation ($name: String!, $level: Int!) {
              setProfileSkill(input: { name: $name, level: $level }) { profileSkill { skill { id } } }
            }
            """, new { name, level }))
        .GetProperty("setProfileSkill").GetProperty("profileSkill").GetProperty("skill").GetProperty("id").GetInt32();

    private static async Task<JsonElement> SetAsync(HttpClient client, int skillId, string name) =>
        (await client.GraphQLAsync(Set, new { skillId, name })).GetProperty("setSkillTranslation");

    private static async Task<string?> NorwegianAsync(HttpClient client, string skillName) =>
        (await client.GraphQLAsync("""
            query ($search: String!) { skills(search: $search) { nodes { name translations { language name } } } }
            """, new { search = skillName }))
        .GetProperty("skills").GetProperty("nodes").EnumerateArray()
        .Single(s => s.GetProperty("name").GetString() == skillName).GetProperty("translations").EnumerateArray()
        .Where(t => t.GetProperty("language").GetString() == "NORWEGIAN").Select(t => t.GetProperty("name").GetString())
        .SingleOrDefault();

    [Fact]
    public async Task Set_Upserts_ThenDeleteRemoves()
    {
        var client = await factory.GetOwnerClientAsync();
        var id = await SkillAsync(client, "TrCode review");

        await SetAsync(client, id, "Kodegjennomgng");
        await SetAsync(client, id, " Kodegjennomgang ");
        Assert.Equal("Kodegjennomgang", await NorwegianAsync(client, "TrCode review"));

        await client.GraphQLAsync("""
            mutation ($skillId: Int!) { deleteSkillTranslation(input: { skillId: $skillId, language: NORWEGIAN }) { skill { id } } }
            """, new { skillId = id });
        Assert.Null(await NorwegianAsync(client, "TrCode review"));
    }

    [Fact]
    public async Task Set_ForeignOrBlank_ReturnsErrors()
    {
        var owner = await factory.GetOwnerClientAsync();
        var id = await SkillAsync(owner, "TrIsolated");

        Assert.Equal("SkillNotFoundError", ApplicationTests.ErrorType(await SetAsync(await factory.GetSecondOwnerClientAsync(), id, "X")));
        Assert.Equal("InvalidSkillCleanupError", ApplicationTests.ErrorType(await SetAsync(owner, id, " ")));
        Assert.Null(await NorwegianAsync(owner, "TrIsolated"));
    }

    [Fact]
    public async Task Prefill_UsesTranslationForDraftLanguage()
    {
        var client = await factory.GetOwnerClientAsync();
        var id = await SkillAsync(client, "TrCustomer service");
        await SetAsync(client, id, "Kundeservice");
        var listing = (await client.GraphQLAsync("""
            mutation ($listing: ListingInput!) { createListing(input: { listing: $listing }) { listing { id } } }
            """, new
        {
            listing = new
            {
                title = "Butikkmedarbeider", companyName = "Translation company", jobRoleName = "Sales", postingLanguage = "NORWEGIAN",
                url = $"https://skill-translation.example/{Guid.NewGuid()}", postedOn = "2026-10-01", deadlineType = "UNSPECIFIED",
                skills = Array.Empty<object>(), languages = Array.Empty<object>(), locations = Array.Empty<object>(),
            },
        })).GetProperty("createListing").GetProperty("listing").GetProperty("id").GetInt32();

        var skills = (await client.GraphQLAsync("""
            query ($listingId: Int!) { tailoredCvDraftFromProfile(listingId: $listingId) { skills { name sourceSkillId } } }
            """, new { listingId = listing })).GetProperty("tailoredCvDraftFromProfile").GetProperty("skills");

        var skill = skills.EnumerateArray().Single(s => s.GetProperty("sourceSkillId").GetInt32() == id);
        Assert.Equal("Kundeservice", skill.GetProperty("name").GetString());
    }

    [Fact]
    public async Task Cleanup_MergeKeepsTranslation_AndAppliesNewOnes()
    {
        var client = await factory.GetOwnerClientAsync();
        var a = await SkillAsync(client, "TrTeamwork");
        var b = await SkillAsync(client, "TrTeam work");
        await SetAsync(client, b, "Samarbeid");
        await SkillAsync(client, "TrTroubleshooting");

        var result = (await client.GraphQLAsync("""
            mutation ($operations: [SkillCleanupOperationInput!]!, $translations: [SkillCleanupTranslationInput!]) {
              cleanUpSkills(input: { operations: $operations, translations: $translations }) { skills { id } errors { __typename } }
            }
            """, new
        {
            operations = new[] { new { skillIds = new[] { a, b }, names = new[] { "TrTeamwork" } } },
            translations = new[] { new { skillName = "trtroubleshooting", language = "NORWEGIAN", name = "Feilsøking" } },
        })).GetProperty("cleanUpSkills");

        Assert.Equal(JsonValueKind.Null, result.GetProperty("errors").ValueKind);
        Assert.Equal("Samarbeid", await NorwegianAsync(client, "TrTeamwork"));
        Assert.Equal("Feilsøking", await NorwegianAsync(client, "TrTroubleshooting"));
    }

    [Fact]
    public async Task Suggest_KeepsOnlyUsefulTranslations()
    {
        var client = await factory.GetGuestClientAsync();
        var id = await SkillAsync(client, "TrSuggest review");
        await SetAsync(client, id, "Gjennomgang");
        factory.Llm.Respond = () => new ChatResponse(new ChatMessage(ChatRole.Assistant, """
            {"operations":[],"translations":[
              {"skillName":"TrSuggest review","language":"NORWEGIAN","name":"Gjennomgang"},
              {"skillName":"TrSuggest review","language":"KLINGON","name":"x"},
              {"skillName":"TrUnknown","language":"NORWEGIAN","name":"x"},
              {"skillName":"TrSuggest review","language":"ENGLISH","name":"Review"}]}
            """));

        var result = (await client.GraphQLAsync("""
            mutation { suggestSkillCleanup(input: {}) { result { status translations { skillName language name } } } }
            """)).GetProperty("suggestSkillCleanup").GetProperty("result");

        Assert.Equal("SUCCEEDED", result.GetProperty("status").GetString());
        var translation = Assert.Single(result.GetProperty("translations").EnumerateArray());
        Assert.Equal("ENGLISH", translation.GetProperty("language").GetString());
        Assert.Contains($"{id}: TrSuggest review [NORWEGIAN: Gjennomgang]", factory.Llm.LastMessages[1].Text);
    }
}
