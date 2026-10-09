using System.Text.Json;
using JobHunt.Api.Tests.Features.Applications;

namespace JobHunt.Api.Tests.Features.Profiles;

public class ProfileSkillTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string SetSkill = """
        mutation ($name: String!, $level: Int!) {
          setProfileSkill(input: { name: $name, level: $level }) {
            profileSkill { level experienceMonths skill { id name } }
            errors { __typename }
          }
        }
        """;

    private const string SetLanguage = """
        mutation ($language: Language!, $level: CefrLevel!) {
          setProfileLanguage(input: { language: $language, level: $level }) { profileLanguage { language level } }
        }
        """;

    private static async Task<JsonElement> SetSkillAsync(HttpClient client, string name, int level) =>
        (await client.GraphQLAsync(SetSkill, new { name, level })).GetProperty("setProfileSkill");

    private static async Task<JsonElement> SkillsAsync(HttpClient client) =>
        (await client.GraphQLAsync("{ profile { skills { level experienceMonths skill { name } } } }"))
        .GetProperty("profile").GetProperty("skills");

    private static int MonthsOf(JsonElement skills, string name) =>
        skills.EnumerateArray().Single(s => s.GetProperty("skill").GetProperty("name").GetString() == name)
            .GetProperty("experienceMonths").GetInt32();

    [Fact]
    public async Task SetSkill_CreatesThenReplacesLevel()
    {
        var client = await factory.GetOwnerClientAsync();

        var created = await SetSkillAsync(client, "Elm", 5);
        await SetSkillAsync(client, "elm", 4);

        Assert.Equal("Elm", created.GetProperty("profileSkill").GetProperty("skill").GetProperty("name").GetString());
        var elm = (await SkillsAsync(client)).EnumerateArray()
            .Where(s => s.GetProperty("skill").GetProperty("name").GetString() == "Elm").ToList();
        Assert.Single(elm);
        Assert.Equal(4, elm[0].GetProperty("level").GetInt32());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public async Task SetSkill_LevelOutOfRange_ReturnsInvalidProfileError(int level)
    {
        var client = await factory.GetOwnerClientAsync();

        Assert.Equal("InvalidProfileError", ApplicationTests.ErrorType(await SetSkillAsync(client, "Go", level)));
    }

    [Fact]
    public async Task Skills_OrderedByLevelThenName()
    {
        var client = await factory.GetGuestClientAsync();
        await SetSkillAsync(client, "CSS", 3);
        await SetSkillAsync(client, "Vue.js", 5);
        await SetSkillAsync(client, "HTML", 3);

        var skills = await SkillsAsync(client);

        Assert.Equal(["Vue.js", "CSS", "HTML"],
            skills.EnumerateArray().Select(s => s.GetProperty("skill").GetProperty("name").GetString()));
    }

    [Fact]
    public async Task RemoveSkill_KeepsEntryTags_AndMissingReturnsNotFound()
    {
        var client = await factory.GetOwnerClientAsync();
        await ProfileEntryTests.AddExperienceAsync(client, skills: ["Vue.js"]);
        var skillId = (await SetSkillAsync(client, "Vue.js", 5))
            .GetProperty("profileSkill").GetProperty("skill").GetProperty("id").GetInt32();
        const string Remove = """
            mutation ($skillId: Int!) { removeProfileSkill(input: { skillId: $skillId }) { skillId errors { __typename } } }
            """;

        var removed = (await client.GraphQLAsync(Remove, new { skillId })).GetProperty("removeProfileSkill");
        var again = (await client.GraphQLAsync(Remove, new { skillId })).GetProperty("removeProfileSkill");

        Assert.Equal(skillId, removed.GetProperty("skillId").GetInt32());
        Assert.Equal("ProfileEntryNotFoundError", ApplicationTests.ErrorType(again));
        var profile = (await client.GraphQLAsync("{ profile { skills { skill { id } } experiences { skills { name } } } }")).GetProperty("profile");
        Assert.DoesNotContain(skillId, profile.GetProperty("skills").EnumerateArray().Select(s => s.GetProperty("skill").GetProperty("id").GetInt32()));
        Assert.Contains(profile.GetProperty("experiences").EnumerateArray(),
            e => e.GetProperty("skills").EnumerateArray().Any(s => s.GetProperty("name").GetString() == "Vue.js"));
    }

    [Fact]
    public async Task ExperienceMonths_CountsTaggedJobs_OverlapsOnce_ProjectsNot()
    {
        var client = await factory.GetOwnerClientAsync();
        await ProfileEntryTests.AddExperienceAsync(client, "A", "2020-01-01", "2020-12-01", ["Single"]);
        await ProfileEntryTests.AddExperienceAsync(client, "B", "2020-01-01", "2020-06-01", ["Overlap"]);
        await ProfileEntryTests.AddExperienceAsync(client, "C", "2020-04-01", "2020-09-01", ["Overlap"]);
        await client.GraphQLAsync("""
            mutation { addProfileProject(input: { name: "Side", startMonth: "2020-01-01", endMonth: "2021-01-01", skillNames: ["ProjectOnly"] }) { profileProject { id } } }
            """);
        foreach (var name in new[] { "Single", "Overlap", "Untagged", "ProjectOnly" })
            await SetSkillAsync(client, name, 3);

        var skills = await SkillsAsync(client);

        Assert.Equal(12, MonthsOf(skills, "Single"));
        Assert.Equal(9, MonthsOf(skills, "Overlap"));
        Assert.Equal(0, MonthsOf(skills, "Untagged"));
        Assert.Equal(0, MonthsOf(skills, "ProjectOnly"));
    }

    [Fact]
    public async Task ExperienceMonths_CurrentJob_CountsToThisMonth()
    {
        var client = await factory.GetOwnerClientAsync();
        var today = DateTime.UtcNow;
        var start = new DateOnly(today.Year, today.Month, 1).AddMonths(-2);
        await ProfileEntryTests.AddExperienceAsync(client, "Now AS", start.ToString("yyyy-MM-dd"), skills: ["Current"]);

        var months = (await SetSkillAsync(client, "Current", 4)).GetProperty("profileSkill").GetProperty("experienceMonths").GetInt32();

        Assert.Equal(3, months);
    }

    [Fact]
    public async Task SetLanguage_AddsReplacesAndRemoves()
    {
        var client = await factory.GetOwnerClientAsync();
        await client.GraphQLAsync(SetLanguage, new { language = "NORWEGIAN", level = "C2" });
        await client.GraphQLAsync(SetLanguage, new { language = "ENGLISH", level = "C1" });
        await client.GraphQLAsync(SetLanguage, new { language = "ENGLISH", level = "B2" });

        var languages = (await client.GraphQLAsync("{ profile { languages { language level } } }"))
            .GetProperty("profile").GetProperty("languages");
        Assert.Equal(["NORWEGIAN:C2", "ENGLISH:B2"], languages.EnumerateArray()
            .Select(l => $"{l.GetProperty("language").GetString()}:{l.GetProperty("level").GetString()}"));

        const string Remove = """
            mutation { removeProfileLanguage(input: { language: ENGLISH }) { language errors { __typename } } }
            """;
        Assert.Equal("ENGLISH", (await client.GraphQLAsync(Remove)).GetProperty("removeProfileLanguage").GetProperty("language").GetString());
        Assert.Equal("ProfileEntryNotFoundError",
            ApplicationTests.ErrorType((await client.GraphQLAsync(Remove)).GetProperty("removeProfileLanguage")));
    }
}
