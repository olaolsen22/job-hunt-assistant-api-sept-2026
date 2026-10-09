using System.Text.Json;
using JobHunt.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace JobHunt.Api.Tests.Features.Profiles;

// Location on entries, and tagged entry skills joining the profile's skills. Skill names are unique per test
// because the owner's profile accumulates.
public class ProfileEntryDetailsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string LocationFields = "location { id city countryCode }";

    private const string AddExperience = $$"""
        mutation ($location: LocationInput, $skillNames: [String!]!) {
          addProfileExperience(input: { employer: "Loc Corp", title: "Dev", location: $location,
                                        startMonth: "2020-01-01", skillNames: $skillNames }) {
            profileExperience { id {{LocationFields}} }
            errors { __typename }
          }
        }
        """;

    private const string UpdateExperience = $$"""
        mutation ($id: Int!, $location: LocationInput, $skillNames: [String!]!) {
          updateProfileExperience(input: { id: $id, employer: "Loc Corp", title: "Dev", location: $location,
                                           startMonth: "2020-01-01", skillNames: $skillNames }) {
            profileExperience { id {{LocationFields}} }
            errors { __typename }
          }
        }
        """;

    private const string AddEducation = $$"""
        mutation ($location: LocationInput) {
          addProfileEducation(input: { school: "Loc School", location: $location, startMonth: "2018-01-01" }) {
            profileEducation { id {{LocationFields}} }
          }
        }
        """;

    private const string AddProject = $$"""
        mutation ($location: LocationInput, $skillNames: [String!]!) {
          addProfileProject(input: { name: "Loc Project", location: $location, skillNames: $skillNames }) {
            profileProject { id {{LocationFields}} }
          }
        }
        """;

    private static readonly object Oslo = new { city = "Oslo", countryCode = "NO" };

    private static async Task<JsonElement> ProfileEntriesAsync(HttpClient client) =>
        (await client.GraphQLAsync($$"""
            { profile { experiences { id {{LocationFields}} } educations { id {{LocationFields}} } projects { id {{LocationFields}} } } }
            """)).GetProperty("profile");

    private static JsonElement Entry(JsonElement list, int id) =>
        list.EnumerateArray().Single(e => e.GetProperty("id").GetInt32() == id);

    private static string? City(JsonElement entry) =>
        entry.GetProperty("location") is { ValueKind: JsonValueKind.Object } l ? l.GetProperty("city").GetString() : null;

    [Fact]
    public async Task Location_CanBeSetOnExperienceEducationAndProject_AndIsReadBack()
    {
        var client = await factory.GetOwnerClientAsync();

        var experience = (await client.GraphQLAsync(AddExperience, new { location = Oslo, skillNames = Array.Empty<string>() }))
            .GetProperty("addProfileExperience").GetProperty("profileExperience");
        var education = (await client.GraphQLAsync(AddEducation, new { location = Oslo }))
            .GetProperty("addProfileEducation").GetProperty("profileEducation");
        var project = (await client.GraphQLAsync(AddProject, new { location = Oslo, skillNames = Array.Empty<string>() }))
            .GetProperty("addProfileProject").GetProperty("profileProject");

        Assert.Equal("Oslo", City(experience));
        Assert.Equal("Oslo", City(education));
        Assert.Equal("Oslo", City(project));
        var profile = await ProfileEntriesAsync(client);
        Assert.Equal("Oslo", City(Entry(profile.GetProperty("experiences"), experience.GetProperty("id").GetInt32())));
        Assert.Equal("Oslo", City(Entry(profile.GetProperty("educations"), education.GetProperty("id").GetInt32())));
        Assert.Equal("Oslo", City(Entry(profile.GetProperty("projects"), project.GetProperty("id").GetInt32())));
    }

    [Fact]
    public async Task UpdateExperience_ReplacesLocation_AndOmittingItClears()
    {
        var client = await factory.GetOwnerClientAsync();
        var id = (await client.GraphQLAsync(AddExperience, new { location = Oslo, skillNames = Array.Empty<string>() }))
            .GetProperty("addProfileExperience").GetProperty("profileExperience").GetProperty("id").GetInt32();

        var moved = (await client.GraphQLAsync(UpdateExperience,
                new { id, location = new { city = "Bergen", countryCode = "NO" }, skillNames = Array.Empty<string>() }))
            .GetProperty("updateProfileExperience").GetProperty("profileExperience");
        var cleared = (await client.GraphQLAsync(UpdateExperience, new { id, location = (object?)null, skillNames = Array.Empty<string>() }))
            .GetProperty("updateProfileExperience").GetProperty("profileExperience");

        Assert.Equal("Bergen", City(moved));
        Assert.Null(City(cleared));
        Assert.Null(City(Entry((await ProfileEntriesAsync(client)).GetProperty("experiences"), id)));
    }

    [Fact]
    public async Task InvalidLocation_ReturnsInvalidLocationError()
    {
        var client = await factory.GetOwnerClientAsync();

        var payload = (await client.GraphQLAsync(AddExperience,
            new { location = new { countryCode = "XX" }, skillNames = Array.Empty<string>() })).GetProperty("addProfileExperience");

        Assert.Equal("InvalidLocationError", payload.GetProperty("errors")[0].GetProperty("__typename").GetString());
    }

    [Fact]
    public async Task DeletingTheLocation_KeepsTheEntry_WithoutLocation()
    {
        var client = await factory.GetOwnerClientAsync();
        var created = (await client.GraphQLAsync(AddExperience,
                new { location = new { city = "Tromsø", countryCode = "NO" }, skillNames = Array.Empty<string>() }))
            .GetProperty("addProfileExperience").GetProperty("profileExperience");
        var id = created.GetProperty("id").GetInt32();
        var locationId = created.GetProperty("location").GetProperty("id").GetInt32();

        using (var scope = factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Locations.IgnoreQueryFilters()
                .Where(l => l.Id == locationId).ExecuteDeleteAsync();

        Assert.Null(City(Entry((await ProfileEntriesAsync(client)).GetProperty("experiences"), id)));
    }

    private static async Task<Dictionary<string, int>> ProfileSkillLevelsAsync(HttpClient client) =>
        (await client.GraphQLAsync("{ profile { skills { level skill { name } } } }")).GetProperty("profile").GetProperty("skills")
        .EnumerateArray().ToDictionary(s => s.GetProperty("skill").GetProperty("name").GetString()!, s => s.GetProperty("level").GetInt32());

    [Fact]
    public async Task TaggingASkillOnExperienceOrProject_AddsItToProfileSkillsAtLevelOne()
    {
        var client = await factory.GetOwnerClientAsync();

        await client.GraphQLAsync(AddExperience, new { location = (object?)null, skillNames = new[] { "SyncSkillExp" } });
        await client.GraphQLAsync(AddProject, new { location = (object?)null, skillNames = new[] { "SyncSkillProj" } });

        var levels = await ProfileSkillLevelsAsync(client);
        Assert.Equal(1, levels["SyncSkillExp"]);
        Assert.Equal(1, levels["SyncSkillProj"]);
    }

    [Fact]
    public async Task TaggingAnExistingProfileSkill_KeepsItsLevel_AndDoesNotDuplicate()
    {
        var client = await factory.GetOwnerClientAsync();
        await client.GraphQLAsync("""mutation { setProfileSkill(input: { name: "SyncKept", level: 4 }) { profileSkill { level } } }""");

        await client.GraphQLAsync(AddProject, new { location = (object?)null, skillNames = new[] { "synckept" } });

        var skills = (await client.GraphQLAsync("{ profile { skills { level skill { name } } } }"))
            .GetProperty("profile").GetProperty("skills").EnumerateArray()
            .Where(s => s.GetProperty("skill").GetProperty("name").GetString()!.Equals("SyncKept", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.Single(skills);
        Assert.Equal(4, skills[0].GetProperty("level").GetInt32());
    }

    [Fact]
    public async Task Untagging_AndRemovingTheProfileSkill_LeaveTheOtherSideAlone()
    {
        var client = await factory.GetOwnerClientAsync();
        var id = (await client.GraphQLAsync(AddExperience, new { location = (object?)null, skillNames = new[] { "SyncUntag" } }))
            .GetProperty("addProfileExperience").GetProperty("profileExperience").GetProperty("id").GetInt32();

        await client.GraphQLAsync(UpdateExperience, new { id, location = (object?)null, skillNames = Array.Empty<string>() });
        Assert.True((await ProfileSkillLevelsAsync(client)).ContainsKey("SyncUntag"));

        var tagged = (await client.GraphQLAsync(UpdateExperience, new { id, location = (object?)null, skillNames = new[] { "SyncUntag" } }))
            .GetProperty("updateProfileExperience").GetProperty("profileExperience").GetProperty("id").GetInt32();
        var skillId = (await client.GraphQLAsync("""{ skills(search: "SyncUntag") { nodes { id } } }"""))
            .GetProperty("skills").GetProperty("nodes")[0].GetProperty("id").GetInt32();
        await client.GraphQLAsync(
            "mutation ($skillId: Int!) { removeProfileSkill(input: { skillId: $skillId }) { skillId } }", new { skillId });

        Assert.False((await ProfileSkillLevelsAsync(client)).ContainsKey("SyncUntag"));
        var experience = (await client.GraphQLAsync("{ profile { experiences { id skills { name } } } }"))
            .GetProperty("profile").GetProperty("experiences");
        Assert.Equal(["SyncUntag"], Entry(experience, tagged).GetProperty("skills").EnumerateArray().Select(s => s.GetProperty("name").GetString()));
    }
}
