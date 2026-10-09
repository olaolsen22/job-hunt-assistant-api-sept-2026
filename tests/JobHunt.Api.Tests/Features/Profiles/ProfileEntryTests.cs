using System.Text.Json;
using JobHunt.Api.Tests.Features.Applications;

namespace JobHunt.Api.Tests.Features.Profiles;

public class ProfileEntryTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string AddExperience = """
        mutation ($employer: String!, $title: String!, $startMonth: LocalDate!, $endMonth: LocalDate,
                  $skillNames: [String!]!) {
          addProfileExperience(input: { employer: $employer, title: $title, startMonth: $startMonth, endMonth: $endMonth,
                                        skillNames: $skillNames }) {
            profileExperience { id employer title startMonth endMonth skills { name } }
            errors { __typename }
          }
        }
        """;

    private const string UpdateExperience = """
        mutation ($id: Int!, $employer: String!, $title: String!, $startMonth: LocalDate!, $endMonth: LocalDate,
                  $skillNames: [String!]!) {
          updateProfileExperience(input: { id: $id, employer: $employer, title: $title, startMonth: $startMonth,
                                           endMonth: $endMonth, skillNames: $skillNames }) {
            profileExperience { title skills { name } }
            errors { __typename }
          }
        }
        """;

    private const string AddEducation = """
        mutation ($school: String!, $startMonth: LocalDate!, $endMonth: LocalDate) {
          addProfileEducation(input: { school: $school, degree: "MSc", field: "Computer Science",
                                       startMonth: $startMonth, endMonth: $endMonth }) {
            profileEducation { id school degree field startMonth endMonth }
            errors { __typename }
          }
        }
        """;

    private const string AddCertification = """
        mutation ($name: String!, $issuedMonth: LocalDate, $expiresMonth: LocalDate, $url: String) {
          addProfileCertification(input: { name: $name, issuer: "AWS", issuedMonth: $issuedMonth,
                                           expiresMonth: $expiresMonth, url: $url }) {
            profileCertification { id name issuer issuedMonth expiresMonth url }
            errors { __typename }
          }
        }
        """;

    private const string AddProject = """
        mutation ($name: String!, $url: String, $startMonth: LocalDate, $endMonth: LocalDate, $skillNames: [String!]!) {
          addProfileProject(input: { name: $name, url: $url, startMonth: $startMonth, endMonth: $endMonth,
                                     skillNames: $skillNames }) {
            profileProject { id name url startMonth skills { name } }
            errors { __typename }
          }
        }
        """;

    internal static object Experience(string employer = "Acme AS", string start = "2020-01-01", string? end = null,
        string[]? skills = null, string title = "Frontend Developer") =>
        new { employer, title, startMonth = start, endMonth = end, skillNames = skills ?? [] };

    internal static async Task<JsonElement> AddExperienceAsync(HttpClient client, string employer = "Acme AS",
        string start = "2020-01-01", string? end = null, string[]? skills = null)
    {
        var payload = (await client.GraphQLAsync(AddExperience, Experience(employer, start, end, skills)))
            .GetProperty("addProfileExperience");
        return payload.GetProperty("profileExperience");
    }

    private static string[] Names(JsonElement list) =>
        [.. list.EnumerateArray().Select(s => s.GetProperty("name").GetString()!)];

    [Fact]
    public async Task AddExperience_DropsDay_AndDoesNotCreateCompany()
    {
        var client = await factory.GetOwnerClientAsync();

        var experience = await AddExperienceAsync(client, "Acme Unique AS", "2021-03-17");

        Assert.Equal("2021-03-01", experience.GetProperty("startMonth").GetString());
        Assert.Equal(JsonValueKind.Null, experience.GetProperty("endMonth").ValueKind);
        var companies = await client.GraphQLAsync("""{ companies(search: "Acme Unique") { totalCount } }""");
        Assert.Equal(0, companies.GetProperty("companies").GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task AddExperience_EndBeforeStart_ReturnsInvalidProfileError_SameMonthAllowed()
    {
        var client = await factory.GetOwnerClientAsync();

        var invalid = (await client.GraphQLAsync(AddExperience, Experience(start: "2022-05-01", end: "2021-01-01")))
            .GetProperty("addProfileExperience");
        var sameMonth = (await client.GraphQLAsync(AddExperience, Experience(start: "2022-05-01", end: "2022-05-31")))
            .GetProperty("addProfileExperience");

        Assert.Equal("InvalidProfileError", ApplicationTests.ErrorType(invalid));
        Assert.Equal("2022-05-01", sameMonth.GetProperty("profileExperience").GetProperty("endMonth").GetString());
    }

    [Fact]
    public async Task SkillTags_AreFoundOrCreated_AndUpdateReplacesThem()
    {
        var client = await factory.GetOwnerClientAsync();

        var experience = await AddExperienceAsync(client, skills: ["Vue.js", "vue.js", "Pinia"]);

        Assert.Equal(["Vue.js", "Pinia"], Names(experience.GetProperty("skills")));
        var skills = await client.GraphQLAsync("""{ skills(search: "Pinia") { nodes { name } } }""");
        Assert.Equal("Pinia", skills.GetProperty("skills").GetProperty("nodes")[0].GetProperty("name").GetString());

        var updated = (await client.GraphQLAsync(UpdateExperience, new
        {
            id = experience.GetProperty("id").GetInt32(), employer = "Acme AS", title = "Lead", startMonth = "2020-01-01",
            endMonth = (string?)null, skillNames = new[] { "React" },
        })).GetProperty("updateProfileExperience").GetProperty("profileExperience");
        Assert.Equal("Lead", updated.GetProperty("title").GetString());
        Assert.Equal(["React"], Names(updated.GetProperty("skills")));
    }

    public static TheoryData<string, object> InvalidExperiences => new()
    {
        { "blank employer", Experience(employer: "  ") },
        { "blank title", Experience(title: " ") },
        { "long employer", Experience(employer: new string('a', 201)) },
        { "blank skill", Experience(skills: [" "]) },
    };

    [Theory]
    [MemberData(nameof(InvalidExperiences))]
    public async Task AddExperience_Invalid_ReturnsInvalidProfileError(string _, object variables)
    {
        var client = await factory.GetOwnerClientAsync();

        var payload = (await client.GraphQLAsync(AddExperience, variables)).GetProperty("addProfileExperience");

        Assert.Equal("InvalidProfileError", ApplicationTests.ErrorType(payload));
        Assert.Equal(JsonValueKind.Null, payload.GetProperty("profileExperience").ValueKind);
    }

    [Fact]
    public async Task DeleteExperience_RemovesIt()
    {
        var client = await factory.GetOwnerClientAsync();
        var id = (await AddExperienceAsync(client)).GetProperty("id").GetInt32();

        var payload = (await client.GraphQLAsync(
            "mutation ($id: Int!) { deleteProfileExperience(input: { id: $id }) { id errors { __typename } } }", new { id }))
            .GetProperty("deleteProfileExperience");

        Assert.Equal(id, payload.GetProperty("id").GetInt32());
        var profile = await client.GraphQLAsync("{ profile { experiences { id } } }");
        Assert.DoesNotContain(id, profile.GetProperty("profile").GetProperty("experiences").EnumerateArray().Select(e => e.GetProperty("id").GetInt32()));
    }

    [Fact]
    public async Task ForeignOrUnknownEntry_ReturnsProfileEntryNotFoundError()
    {
        var owner = await factory.GetOwnerClientAsync();
        var guest = await factory.GetGuestClientAsync();
        var id = (await AddExperienceAsync(owner, "Owner Corp")).GetProperty("id").GetInt32();

        var update = (await guest.GraphQLAsync(UpdateExperience, new
        {
            id, employer = "Hijacked", title = "X", startMonth = "2020-01-01", endMonth = (string?)null,
            skillNames = Array.Empty<string>(),
        })).GetProperty("updateProfileExperience");
        Assert.Equal("ProfileEntryNotFoundError", ApplicationTests.ErrorType(update));

        foreach (var kind in new[] { "Experience", "Education", "Certification", "Project" })
        {
            var delete = (await guest.GraphQLAsync(
                $"mutation ($id: Int!) {{ deleteProfile{kind}(input: {{ id: $id }}) {{ id errors {{ __typename }} }} }}",
                new { id })).GetProperty($"deleteProfile{kind}");
            Assert.Equal("ProfileEntryNotFoundError", ApplicationTests.ErrorType(delete));
        }

        var profile = await owner.GraphQLAsync("{ profile { experiences { employer } } }");
        Assert.Contains("Owner Corp", profile.GetProperty("profile").GetProperty("experiences").EnumerateArray().Select(e => e.GetProperty("employer").GetString()));
    }

    [Fact]
    public async Task Education_OngoingStudy()
    {
        var client = await factory.GetOwnerClientAsync();

        var education = (await client.GraphQLAsync(AddEducation, new { school = " NTNU ", startMonth = "2024-08-20", endMonth = (string?)null }))
            .GetProperty("addProfileEducation").GetProperty("profileEducation");

        Assert.Equal("NTNU", education.GetProperty("school").GetString());
        Assert.Equal("2024-08-01", education.GetProperty("startMonth").GetString());
        Assert.Equal(JsonValueKind.Null, education.GetProperty("endMonth").ValueKind);
    }

    [Fact]
    public async Task Education_UpdateAndDelete()
    {
        var client = await factory.GetOwnerClientAsync();
        var id = (await client.GraphQLAsync(AddEducation, new { school = "NTNU", startMonth = "2015-08-01", endMonth = "2020-06-01" }))
            .GetProperty("addProfileEducation").GetProperty("profileEducation").GetProperty("id").GetInt32();

        var updated = (await client.GraphQLAsync("""
            mutation ($id: Int!) {
              updateProfileEducation(input: { id: $id, school: "UiO", startMonth: "2015-08-01" }) {
                profileEducation { school degree endMonth } errors { __typename }
              }
            }
            """, new { id })).GetProperty("updateProfileEducation").GetProperty("profileEducation");
        Assert.Equal("UiO", updated.GetProperty("school").GetString());
        Assert.Equal(JsonValueKind.Null, updated.GetProperty("degree").ValueKind);

        var deleted = (await client.GraphQLAsync(
            "mutation ($id: Int!) { deleteProfileEducation(input: { id: $id }) { id } }", new { id }));
        Assert.Equal(id, deleted.GetProperty("deleteProfileEducation").GetProperty("id").GetInt32());
    }

    [Fact]
    public async Task Certification_SavesMonth_AndRejectsExpiryBeforeIssued()
    {
        var client = await factory.GetOwnerClientAsync();

        var certification = (await client.GraphQLAsync(AddCertification,
                new { name = "AWS Certified Developer", issuedMonth = "2023-06-15", expiresMonth = (string?)null, url = "https://aws.example/cert" }))
            .GetProperty("addProfileCertification").GetProperty("profileCertification");
        var invalid = (await client.GraphQLAsync(AddCertification,
                new { name = "Old", issuedMonth = "2023-06-01", expiresMonth = "2022-06-01", url = (string?)null }))
            .GetProperty("addProfileCertification");
        var badUrl = (await client.GraphQLAsync(AddCertification,
                new { name = "Old", issuedMonth = (string?)null, expiresMonth = (string?)null, url = "not a url" }))
            .GetProperty("addProfileCertification");

        Assert.Equal("2023-06-01", certification.GetProperty("issuedMonth").GetString());
        Assert.Equal("InvalidProfileError", ApplicationTests.ErrorType(invalid));
        Assert.Equal("InvalidProfileError", ApplicationTests.ErrorType(badUrl));
    }

    [Fact]
    public async Task Project_TagsSkills_AndUpdates()
    {
        var client = await factory.GetOwnerClientAsync();

        var project = (await client.GraphQLAsync(AddProject,
                new { name = "Job Hunt Assistant", url = "https://github.com/ola/jobhunt", startMonth = "2026-09-26", endMonth = (string?)null, skillNames = new[] { ".NET", "Vue.js" } }))
            .GetProperty("addProfileProject").GetProperty("profileProject");
        var invalid = (await client.GraphQLAsync(AddProject,
                new { name = "X", url = "ftp://x", startMonth = (string?)null, endMonth = (string?)null, skillNames = Array.Empty<string>() }))
            .GetProperty("addProfileProject");

        Assert.Equal([".NET", "Vue.js"], Names(project.GetProperty("skills")));
        Assert.Equal("2026-09-01", project.GetProperty("startMonth").GetString());
        Assert.Equal("InvalidProfileError", ApplicationTests.ErrorType(invalid));

        var updated = (await client.GraphQLAsync("""
            mutation ($id: Int!) {
              updateProfileProject(input: { id: $id, name: "Renamed", skillNames: ["Go"] }) {
                profileProject { name skills { name } } errors { __typename }
              }
            }
            """, new { id = project.GetProperty("id").GetInt32() }))
            .GetProperty("updateProfileProject").GetProperty("profileProject");
        Assert.Equal("Renamed", updated.GetProperty("name").GetString());
        Assert.Equal(["Go"], Names(updated.GetProperty("skills")));
    }

    [Fact]
    public async Task Entries_AreOrdered_CurrentFirst_UndatedLast()
    {
        var client = await factory.GetGuestClientAsync();
        await AddExperienceAsync(client, "Old Job", "2014-01-01", "2016-12-01");
        await AddExperienceAsync(client, "Current Job", "2021-01-01");
        await AddExperienceAsync(client, "Middle Job", "2018-01-01", "2020-12-01");
        await client.GraphQLAsync(AddCertification, new { name = "Undated", issuedMonth = (string?)null, expiresMonth = (string?)null, url = (string?)null });
        await client.GraphQLAsync(AddCertification, new { name = "Older", issuedMonth = "2019-01-01", expiresMonth = (string?)null, url = (string?)null });
        await client.GraphQLAsync(AddCertification, new { name = "Newer", issuedMonth = "2023-01-01", expiresMonth = (string?)null, url = (string?)null });
        await client.GraphQLAsync(AddProject, new { name = "Undated", url = (string?)null, startMonth = (string?)null, endMonth = (string?)null, skillNames = Array.Empty<string>() });
        await client.GraphQLAsync(AddProject, new { name = "Ongoing", url = (string?)null, startMonth = "2020-01-01", endMonth = (string?)null, skillNames = Array.Empty<string>() });
        await client.GraphQLAsync(AddProject, new { name = "Done", url = (string?)null, startMonth = "2022-01-01", endMonth = "2022-06-01", skillNames = Array.Empty<string>() });

        var profile = (await client.GraphQLAsync(
            "{ profile { experiences { employer } certifications { name } projects { name } } }")).GetProperty("profile");

        Assert.Equal(["Current Job", "Middle Job", "Old Job"],
            profile.GetProperty("experiences").EnumerateArray().Select(e => e.GetProperty("employer").GetString()));
        Assert.Equal(["Newer", "Older", "Undated"], Names(profile.GetProperty("certifications")));
        Assert.Equal(["Ongoing", "Done", "Undated"], Names(profile.GetProperty("projects")));
    }
}
