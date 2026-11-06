using System.Text.Json;
using JobHunt.Api.Tests.Features.Applications;

namespace JobHunt.Api.Tests.Features.Profiles;

public class ProfileEntryTranslationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string SetExperience = """
        mutation ($experienceId: Int!, $language: Language!, $title: String!) {
          setProfileExperienceTranslation(input: { experienceId: $experienceId, language: $language, title: $title }) {
            profileExperienceTranslation { id language title sourceTitle isStale isMachine }
            errors { __typename }
          }
        }
        """;

    private const string SetEducation = """
        mutation ($educationId: Int!, $degree: String, $field: String) {
          setProfileEducationTranslation(input: { educationId: $educationId, language: NORWEGIAN, degree: $degree, field: $field }) {
            profileEducationTranslation { id degree field sourceDegree sourceField isDegreeStale isFieldStale }
            errors { __typename }
          }
        }
        """;

    internal static async Task<int> AddEducationAsync(HttpClient client, string school, string? degree, string? field) =>
        (await client.GraphQLAsync("""
            mutation ($school: String!, $degree: String, $field: String) {
              addProfileEducation(input: { school: $school, degree: $degree, field: $field, startMonth: "2015-08-01" }) {
                profileEducation { id }
              }
            }
            """, new { school, degree, field }))
            .GetProperty("addProfileEducation").GetProperty("profileEducation").GetProperty("id").GetInt32();

    private static async Task<JsonElement> SetExperienceAsync(HttpClient client, int experienceId, string title,
        string language = "NORWEGIAN") =>
        (await client.GraphQLAsync(SetExperience, new { experienceId, language, title })).GetProperty("setProfileExperienceTranslation");

    private static async Task<JsonElement> SetEducationAsync(HttpClient client, int educationId, string? degree, string? field) =>
        (await client.GraphQLAsync(SetEducation, new { educationId, degree, field })).GetProperty("setProfileEducationTranslation");

    [Fact]
    public async Task SetExperience_Upserts_AndGoesStaleWhenTitleChanges()
    {
        var client = await factory.GetOwnerClientAsync();
        var id = (await ProfileEntryTests.AddExperienceAsync(client, $"Title {Guid.NewGuid()}")).GetProperty("id").GetInt32();
        var first = (await SetExperienceAsync(client, id, "Utvikler")).GetProperty("profileExperienceTranslation");

        var second = (await SetExperienceAsync(client, id, "  Frontendutvikler ")).GetProperty("profileExperienceTranslation");
        await client.GraphQLAsync("""
            mutation ($id: Int!) {
              updateProfileExperience(input: { id: $id, employer: "Renamed", title: "Lead", startMonth: "2020-01-01", skillNames: [] }) {
                profileExperience { id }
              }
            }
            """, new { id });

        Assert.Equal(first.GetProperty("id").GetInt32(), second.GetProperty("id").GetInt32());
        Assert.Equal("Frontendutvikler", second.GetProperty("title").GetString());
        Assert.Equal("Frontend Developer", second.GetProperty("sourceTitle").GetString());
        Assert.False(second.GetProperty("isStale").GetBoolean());
        var listed = (await client.GraphQLAsync("{ profile { experiences { id translations { title isStale } } } }"))
            .GetProperty("profile").GetProperty("experiences").EnumerateArray()
            .Single(e => e.GetProperty("id").GetInt32() == id).GetProperty("translations").EnumerateArray().Single();
        Assert.Equal("Frontendutvikler", listed.GetProperty("title").GetString());
        Assert.True(listed.GetProperty("isStale").GetBoolean());
    }

    [Fact]
    public async Task SetExperience_RejectsForeignContentLanguageAndBlank()
    {
        var client = await factory.GetOwnerClientAsync();
        var id = (await ProfileEntryTests.AddExperienceAsync(client, $"Reject {Guid.NewGuid()}")).GetProperty("id").GetInt32();
        var foreignId = (await ProfileEntryTests.AddExperienceAsync(await factory.GetSecondOwnerClientAsync(), "Foreign AS"))
            .GetProperty("id").GetInt32();

        Assert.Equal("ProfileEntryNotFoundError", ApplicationTests.ErrorType(await SetExperienceAsync(client, foreignId, "Fremmed")));
        Assert.Equal("InvalidProfileError", ApplicationTests.ErrorType(await SetExperienceAsync(client, id, "Same", "ENGLISH")));
        Assert.Equal("InvalidProfileError", ApplicationTests.ErrorType(await SetExperienceAsync(client, id, "  ")));
    }

    [Fact]
    public async Task SetEducation_FullReplace_StalePerField_AndRejectsSourceless()
    {
        var client = await factory.GetOwnerClientAsync();
        var id = await AddEducationAsync(client, $"NTNU {Guid.NewGuid()}", "Bachelor", "Computer Science");
        var noField = await AddEducationAsync(client, $"UiO {Guid.NewGuid()}", "Master", null);

        await SetEducationAsync(client, id, "Bachelor", "Informatikk");
        var replaced = (await SetEducationAsync(client, id, "Bachelorgrad", null)).GetProperty("profileEducationTranslation");

        Assert.Equal("Bachelorgrad", replaced.GetProperty("degree").GetString());
        Assert.Equal(JsonValueKind.Null, replaced.GetProperty("field").ValueKind);
        Assert.False(replaced.GetProperty("isFieldStale").GetBoolean());
        Assert.Equal("InvalidProfileError", ApplicationTests.ErrorType(await SetEducationAsync(client, noField, null, "Felt")));
        Assert.Equal("InvalidProfileError", ApplicationTests.ErrorType(await SetEducationAsync(client, id, " ", null)));
    }

    [Fact]
    public async Task DeleteEducation_RemovesOwn_ForeignIsNotFound()
    {
        var client = await factory.GetOwnerClientAsync();
        var id = await AddEducationAsync(client, $"Delete {Guid.NewGuid()}", "Bachelor", null);
        var translationId = (await SetEducationAsync(client, id, "Bachelor", null))
            .GetProperty("profileEducationTranslation").GetProperty("id").GetInt32();
        const string delete = """
            mutation ($id: Int!) { deleteProfileEducationTranslation(input: { id: $id }) { id errors { __typename } } }
            """;

        var foreign = (await (await factory.GetSecondOwnerClientAsync()).GraphQLAsync(delete, new { id = translationId }))
            .GetProperty("deleteProfileEducationTranslation");
        var own = (await client.GraphQLAsync(delete, new { id = translationId })).GetProperty("deleteProfileEducationTranslation");

        Assert.Equal("ProfileEntryNotFoundError", ApplicationTests.ErrorType(foreign));
        Assert.Equal(translationId, own.GetProperty("id").GetInt32());
    }
}
