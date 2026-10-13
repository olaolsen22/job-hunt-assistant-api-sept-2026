using System.Text.Json;
using JobHunt.Api.Tests.Features.Applications;

namespace JobHunt.Api.Tests.Features.Profiles;

public class ProfileClearTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string AllSections =
        "[SUMMARY, EXPERIENCES, EDUCATIONS, CERTIFICATIONS, PROJECTS, SKILLS, LANGUAGES, REFERENCES, HOBBIES]";

    private const string ProfileFields = """
        fullName headline summary hobbies links { url }
        experiences { id } educations { id } certifications { id } projects { id }
        skills { level } languages { name } references { name } translations { language }
        """;

    private const string Seed = """
        mutation {
          updateProfile(input: { fullName: "Kari Nordmann", headline: "Developer", summary: "Builds things",
                                 hobbies: ["Climbing"], links: [{ kind: GITHUB, url: "https://github.com/kari" }] }) {
            errors { __typename }
          }
          setProfileTranslation(input: { language: NORWEGIAN, headline: "Utvikler" }) { errors { __typename } }
          addProfileExperience(input: { employer: "Acme AS", title: "Developer", startMonth: "2020-01-01",
                                        skillNames: ["C#"] }) {
            profileExperience { id }
          }
          addProfileEducation(input: { school: "NTNU", startMonth: "2015-08-01" }) { errors { __typename } }
          addProfileCertification(input: { name: "AWS SAA" }) { errors { __typename } }
          addProfileProject(input: { name: "Job hunt", skillNames: [] }) { errors { __typename } }
          setProfileSkill(input: { name: "C#", level: 4 }) { errors { __typename } }
          setProfileLanguage(input: { name: "Norwegian", written: NATIVE, spoken: NATIVE }) { errors { __typename } }
          addProfileReference(input: { name: "Per Hansen", relationship: "Manager" }) { errors { __typename } }
        }
        """;

    private static async Task SeedAsync(HttpClient client)
    {
        // Clear first, so a test never depends on what another test in the class left behind.
        await ClearAsync(client, AllSections);
        var data = await client.GraphQLAsync(Seed);
        var experienceId = data.GetProperty("addProfileExperience").GetProperty("profileExperience").GetProperty("id").GetInt32();
        await client.GraphQLAsync("""
            mutation ($id: Int!) { addProfileBullet(input: { kind: EXPERIENCE, entryId: $id, text: "Shipped it" }) { errors { __typename } } }
            """, new { id = experienceId });
    }

    private static async Task<JsonElement> ClearAsync(HttpClient client, string sections) =>
        (await client.GraphQLAsync($$"""
            mutation { clearProfileSections(input: { sections: {{sections}} }) { profile { {{ProfileFields}} } errors { __typename } } }
            """)).GetProperty("clearProfileSections");

    private static async Task<JsonElement> ProfileAsync(HttpClient client) =>
        (await client.GraphQLAsync($$"""{ profile { {{ProfileFields}} } }""")).GetProperty("profile");

    private static int Count(JsonElement profile, string list) => profile.GetProperty(list).GetArrayLength();

    [Fact]
    public async Task Clear_SelectedSections_KeepsTheRest()
    {
        var client = await factory.GetOwnerClientAsync();
        await SeedAsync(client);

        var profile = (await ClearAsync(client, "[EXPERIENCES, SKILLS]")).GetProperty("profile");

        Assert.Equal(0, Count(profile, "experiences"));
        Assert.Equal(0, Count(profile, "skills"));
        Assert.Equal(1, Count(profile, "educations"));
        Assert.Equal(1, Count(profile, "languages"));
        Assert.Equal(1, Count(profile, "references"));
        Assert.Equal(1, Count(profile, "translations"));
        Assert.Equal("Developer", profile.GetProperty("headline").GetString());
        Assert.Equal(1, Count(profile, "hobbies"));
    }

    [Fact]
    public async Task Clear_AllSections_KeepsPersonalDetailsAndLinks()
    {
        var client = await factory.GetOwnerClientAsync();
        await SeedAsync(client);

        Assert.Equal(JsonValueKind.Null, (await ClearAsync(client, AllSections)).GetProperty("errors").ValueKind);

        var profile = await ProfileAsync(client);
        foreach (var list in new[] { "experiences", "educations", "certifications", "projects", "skills", "languages",
                     "references", "translations", "hobbies" })
            Assert.Equal(0, Count(profile, list));
        Assert.Equal(JsonValueKind.Null, profile.GetProperty("headline").ValueKind);
        Assert.Equal(JsonValueKind.Null, profile.GetProperty("summary").ValueKind);
        Assert.Equal("Kari Nordmann", profile.GetProperty("fullName").GetString());
        Assert.Equal(1, Count(profile, "links"));
    }

    [Fact]
    public async Task Clear_LeavesOtherUsersProfileAlone()
    {
        var other = await factory.GetSecondOwnerClientAsync();
        await SeedAsync(other);
        var owner = await factory.GetOwnerClientAsync();

        await ClearAsync(owner, AllSections);

        var profile = await ProfileAsync(other);
        Assert.Equal(1, Count(profile, "experiences"));
        Assert.Equal(1, Count(profile, "skills"));
        Assert.Equal(1, Count(profile, "references"));
        Assert.Equal(1, Count(profile, "translations"));
        Assert.Equal("Developer", profile.GetProperty("headline").GetString());
    }

    [Fact]
    public async Task Clear_NoSections_ReturnsInvalidProfileError()
    {
        var client = await factory.GetOwnerClientAsync();

        Assert.Equal("InvalidProfileError", ApplicationTests.ErrorType(await ClearAsync(client, "[]")));
    }
}
