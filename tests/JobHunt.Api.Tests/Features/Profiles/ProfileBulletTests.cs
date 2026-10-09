using System.Net.Http.Json;
using System.Text.Json;
using JobHunt.Api.Data;
using JobHunt.Api.Tests.Features.Applications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace JobHunt.Api.Tests.Features.Profiles;

// Every test makes its own entries, so they can share one factory.
public class ProfileBulletTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string AddBullet = """
        mutation ($kind: ProfileEntryKind!, $entryId: Int!, $text: String!) {
          addProfileBullet(input: { kind: $kind, entryId: $entryId, text: $text }) {
            profileBullet { id text position }
            errors { __typename }
          }
        }
        """;

    private const string UpdateBullet = """
        mutation ($id: Int!, $text: String!) {
          updateProfileBullet(input: { id: $id, text: $text }) {
            profileBullet { id text position }
            errors { __typename }
          }
        }
        """;

    private const string MoveBullet = """
        mutation ($id: Int!, $position: Int!) {
          moveProfileBullet(input: { id: $id, position: $position }) {
            bullets { id text position }
            errors { __typename }
          }
        }
        """;

    private const string DeleteBullet = """
        mutation ($id: Int!) { deleteProfileBullet(input: { id: $id }) { id errors { __typename } } }
        """;

    private static async Task<int> ExperienceIdAsync(HttpClient client, string employer = "Bullet Corp") =>
        (await ProfileEntryTests.AddExperienceAsync(client, employer)).GetProperty("id").GetInt32();

    private static async Task<int> EntryIdAsync(HttpClient client, string kind) => kind switch
    {
        "EXPERIENCE" => await ExperienceIdAsync(client),
        "EDUCATION" => Id(await client.GraphQLAsync("""
            mutation { addProfileEducation(input: { school: "S", startMonth: "2020-01-01" }) { profileEducation { id } } }
            """), "addProfileEducation", "profileEducation"),
        "CERTIFICATION" => Id(await client.GraphQLAsync("""
            mutation { addProfileCertification(input: { name: "C" }) { profileCertification { id } } }
            """), "addProfileCertification", "profileCertification"),
        _ => Id(await client.GraphQLAsync("""
            mutation { addProfileProject(input: { name: "P", skillNames: [] }) { profileProject { id } } }
            """), "addProfileProject", "profileProject"),
    };

    private static int Id(JsonElement data, string mutation, string field) =>
        data.GetProperty(mutation).GetProperty(field).GetProperty("id").GetInt32();

    private static async Task<JsonElement> AddAsync(HttpClient client, string kind, int entryId, string text) =>
        (await client.GraphQLAsync(AddBullet, new { kind, entryId, text })).GetProperty("addProfileBullet");

    private static async Task<int> AddOkAsync(HttpClient client, string kind, int entryId, string text) =>
        (await AddAsync(client, kind, entryId, text)).GetProperty("profileBullet").GetProperty("id").GetInt32();

    // The experience as the profile query returns it, so a test sees what the UI sees.
    private static async Task<string[]> TextsAsync(HttpClient client, string list, int entryId)
    {
        var profile = (await client.GraphQLAsync($"{{ profile {{ {list} {{ id bullets {{ text }} }} }} }}")).GetProperty("profile");
        var entry = profile.GetProperty(list).EnumerateArray().Single(e => e.GetProperty("id").GetInt32() == entryId);
        return [.. entry.GetProperty("bullets").EnumerateArray().Select(b => b.GetProperty("text").GetString()!)];
    }

    private static string[] Texts(JsonElement bullets) =>
        [.. bullets.EnumerateArray().Select(b => b.GetProperty("text").GetString()!)];

    private static int[] Positions(JsonElement bullets) =>
        [.. bullets.EnumerateArray().Select(b => b.GetProperty("position").GetInt32())];

    [Fact]
    public async Task NewEntry_HasNoBullets()
    {
        var client = await factory.GetOwnerClientAsync();
        var id = await ExperienceIdAsync(client);

        Assert.Empty(await TextsAsync(client, "experiences", id));
    }

    [Theory]
    [InlineData("EXPERIENCE", "experiences")]
    [InlineData("EDUCATION", "educations")]
    [InlineData("CERTIFICATION", "certifications")]
    [InlineData("PROJECT", "projects")]
    public async Task EachKind_OwnsItsBullets(string kind, string list)
    {
        var client = await factory.GetOwnerClientAsync();
        var mine = await EntryIdAsync(client, kind);
        var other = await EntryIdAsync(client, kind);

        await AddOkAsync(client, kind, mine, "Mine");
        await AddOkAsync(client, kind, other, "Other");

        Assert.Equal(["Mine"], await TextsAsync(client, list, mine));
        Assert.Equal(["Other"], await TextsAsync(client, list, other));
    }

    [Fact]
    public async Task Add_AppendsTrimmed_InOrder()
    {
        var client = await factory.GetOwnerClientAsync();
        var id = await ExperienceIdAsync(client);

        await AddOkAsync(client, "EXPERIENCE", id, "A");
        await AddOkAsync(client, "EXPERIENCE", id, "B");
        var third = (await AddAsync(client, "EXPERIENCE", id, "  C  ")).GetProperty("profileBullet");

        Assert.Equal("C", third.GetProperty("text").GetString());
        Assert.Equal(2, third.GetProperty("position").GetInt32());
        Assert.Equal(["A", "B", "C"], await TextsAsync(client, "experiences", id));
    }

    [Fact]
    public async Task Add_WrongKindUnknownOrForeignEntry_ReturnsNotFound()
    {
        var owner = await factory.GetOwnerClientAsync();
        var guest = await factory.GetGuestClientAsync();
        var experienceId = await ExperienceIdAsync(owner);

        var wrongKind = await AddAsync(owner, "EDUCATION", experienceId, "X");
        var unknown = await AddAsync(owner, "EXPERIENCE", 999_999, "X");
        var foreign = await AddAsync(guest, "EXPERIENCE", experienceId, "X");

        foreach (var payload in new[] { wrongKind, unknown, foreign })
            Assert.Equal("ProfileEntryNotFoundError", ApplicationTests.ErrorType(payload));
        Assert.Empty(await TextsAsync(owner, "experiences", experienceId));
    }

    [Fact]
    public async Task Add_Invalid_ReturnsInvalidProfileError_AndChangesNothing()
    {
        var client = await factory.GetOwnerClientAsync();
        var id = await ExperienceIdAsync(client);

        Assert.Equal("InvalidProfileError", ApplicationTests.ErrorType(await AddAsync(client, "EXPERIENCE", id, "   ")));
        Assert.Equal("InvalidProfileError", ApplicationTests.ErrorType(await AddAsync(client, "EXPERIENCE", id, new string('a', 501))));
        Assert.Empty(await TextsAsync(client, "experiences", id));

        await AddOkAsync(client, "EXPERIENCE", id, new string('a', 500));
        for (var i = 1; i < 20; i++)
            await AddOkAsync(client, "EXPERIENCE", id, $"B{i}");
        Assert.Equal("InvalidProfileError", ApplicationTests.ErrorType(await AddAsync(client, "EXPERIENCE", id, "21st")));
        Assert.Equal(20, (await TextsAsync(client, "experiences", id)).Length);
    }

    [Fact]
    public async Task Update_KeepsIdAndPosition()
    {
        var client = await factory.GetOwnerClientAsync();
        var id = await ExperienceIdAsync(client);
        await AddOkAsync(client, "EXPERIENCE", id, "First");
        var bulletId = await AddOkAsync(client, "EXPERIENCE", id, "Second");

        var updated = (await client.GraphQLAsync(UpdateBullet, new { id = bulletId, text = " Changed " }))
            .GetProperty("updateProfileBullet").GetProperty("profileBullet");

        Assert.Equal(bulletId, updated.GetProperty("id").GetInt32());
        Assert.Equal(1, updated.GetProperty("position").GetInt32());
        Assert.Equal("Changed", updated.GetProperty("text").GetString());
        Assert.Equal(["First", "Changed"], await TextsAsync(client, "experiences", id));
    }

    [Fact]
    public async Task UpdateAndDelete_UnknownOrForeignBullet_ReturnNotFound()
    {
        var owner = await factory.GetOwnerClientAsync();
        var guest = await factory.GetGuestClientAsync();
        var bulletId = await AddOkAsync(owner, "EXPERIENCE", await ExperienceIdAsync(owner), "Owner's");

        foreach (var (client, id) in new[] { (owner, 999_999), (guest, bulletId) })
        {
            var update = (await client.GraphQLAsync(UpdateBullet, new { id, text = "X" })).GetProperty("updateProfileBullet");
            var move = (await client.GraphQLAsync(MoveBullet, new { id, position = 0 })).GetProperty("moveProfileBullet");
            var delete = (await client.GraphQLAsync(DeleteBullet, new { id })).GetProperty("deleteProfileBullet");
            foreach (var payload in new[] { update, move, delete })
                Assert.Equal("ProfileEntryNotFoundError", ApplicationTests.ErrorType(payload));
        }
    }

    [Fact]
    public async Task Delete_ClosesTheGap()
    {
        var client = await factory.GetOwnerClientAsync();
        var id = await ExperienceIdAsync(client);
        await AddOkAsync(client, "EXPERIENCE", id, "A");
        var b = await AddOkAsync(client, "EXPERIENCE", id, "B");
        await AddOkAsync(client, "EXPERIENCE", id, "C");

        var deleted = (await client.GraphQLAsync(DeleteBullet, new { id = b })).GetProperty("deleteProfileBullet");

        Assert.Equal(b, deleted.GetProperty("id").GetInt32());
        var profile = (await client.GraphQLAsync("{ profile { experiences { id bullets { text position } } } }")).GetProperty("profile");
        var bullets = profile.GetProperty("experiences").EnumerateArray().Single(e => e.GetProperty("id").GetInt32() == id).GetProperty("bullets");
        Assert.Equal(["A", "C"], Texts(bullets));
        Assert.Equal([0, 1], Positions(bullets));
    }

    [Theory]
    [InlineData("EXPERIENCE")]
    [InlineData("EDUCATION")]
    [InlineData("CERTIFICATION")]
    [InlineData("PROJECT")]
    public async Task DeletingTheEntry_DeletesItsBullets(string kind)
    {
        var client = await factory.GetOwnerClientAsync();
        var entryId = await EntryIdAsync(client, kind);
        var bulletId = await AddOkAsync(client, kind, entryId, "Goes with it");
        var keptEntry = await EntryIdAsync(client, kind);
        var keptBullet = await AddOkAsync(client, kind, keptEntry, "Stays");

        var title = char.ToUpper(kind[0]) + kind[1..].ToLower();
        await client.GraphQLAsync($"mutation ($id: Int!) {{ deleteProfile{title}(input: {{ id: $id }}) {{ id }} }}", new { id = entryId });

        using var scope = factory.Services.CreateScope();
        var ids = await scope.ServiceProvider.GetRequiredService<AppDbContext>().ProfileBullets.IgnoreQueryFilters()
            .Where(b => b.Id == bulletId || b.Id == keptBullet).Select(b => b.Id).ToListAsync();
        Assert.Equal([keptBullet], ids);
    }

    [Fact]
    public async Task Move_ReordersAndReturnsTheEntrysBullets()
    {
        var client = await factory.GetOwnerClientAsync();
        var id = await ExperienceIdAsync(client);
        var a = await AddOkAsync(client, "EXPERIENCE", id, "A");
        var b = await AddOkAsync(client, "EXPERIENCE", id, "B");
        var c = await AddOkAsync(client, "EXPERIENCE", id, "C");

        var up = (await client.GraphQLAsync(MoveBullet, new { id = c, position = 0 })).GetProperty("moveProfileBullet").GetProperty("bullets");
        Assert.Equal(["C", "A", "B"], Texts(up));
        Assert.Equal([0, 1, 2], Positions(up));

        var toEnd = (await client.GraphQLAsync(MoveBullet, new { id = c, position = 2 })).GetProperty("moveProfileBullet").GetProperty("bullets");
        Assert.Equal(["A", "B", "C"], Texts(toEnd));

        await client.GraphQLAsync(MoveBullet, new { id = a, position = 1 });
        await client.GraphQLAsync(MoveBullet, new { id = b, position = 2 });
        await client.GraphQLAsync(MoveBullet, new { id = c, position = 1 });
        var final = (await client.GraphQLAsync("{ profile { experiences { id bullets { id position } } } }")).GetProperty("profile")
            .GetProperty("experiences").EnumerateArray().Single(e => e.GetProperty("id").GetInt32() == id).GetProperty("bullets");
        Assert.Equal([0, 1, 2], Positions(final));
        Assert.Equal(new[] { a, b, c }.Order(), final.EnumerateArray().Select(x => x.GetProperty("id").GetInt32()).Order());
    }

    [Theory]
    [InlineData(3)]
    [InlineData(-1)]
    public async Task Move_OutOfRange_ReturnsInvalidProfileError_AndKeepsOrder(int position)
    {
        var client = await factory.GetOwnerClientAsync();
        var id = await ExperienceIdAsync(client);
        var first = await AddOkAsync(client, "EXPERIENCE", id, "A");
        await AddOkAsync(client, "EXPERIENCE", id, "B");
        await AddOkAsync(client, "EXPERIENCE", id, "C");

        var payload = (await client.GraphQLAsync(MoveBullet, new { id = first, position })).GetProperty("moveProfileBullet");

        Assert.Equal("InvalidProfileError", ApplicationTests.ErrorType(payload));
        Assert.Equal(["A", "B", "C"], await TextsAsync(client, "experiences", id));
    }

    [Fact]
    public async Task UpdatingTheEntry_LeavesBulletsAndIds()
    {
        var client = await factory.GetOwnerClientAsync();
        var id = await ExperienceIdAsync(client);
        var a = await AddOkAsync(client, "EXPERIENCE", id, "A");
        var b = await AddOkAsync(client, "EXPERIENCE", id, "B");

        // The entry returned by the mutation lists the same bullets the profile query does.
        var updated = (await client.GraphQLAsync("""
            mutation ($id: Int!) {
              updateProfileExperience(input: { id: $id, employer: "Renamed", title: "Lead", startMonth: "2020-01-01", skillNames: [] }) {
                profileExperience { employer bullets { id text position } }
              }
            }
            """, new { id })).GetProperty("updateProfileExperience").GetProperty("profileExperience");

        Assert.Equal("Renamed", updated.GetProperty("employer").GetString());
        Assert.Equal([a, b], updated.GetProperty("bullets").EnumerateArray().Select(x => x.GetProperty("id").GetInt32()));
        Assert.Equal(["A", "B"], await TextsAsync(client, "experiences", id));
    }

    [Fact]
    public async Task OldFields_AreGone()
    {
        var client = await factory.GetOwnerClientAsync();

        // GraphQLAsync fails the test on top-level errors, so post by hand: an unknown field is exactly that error.
        var response = await client.PostAsJsonAsync("/graphql", new
        {
            query = "{ profile { experiences { description } educations { description } projects { description } } }",
        });
        var highlights = await client.PostAsJsonAsync("/graphql", new { query = "{ profile { experiences { highlights } } }" });

        foreach (var r in new[] { response, highlights })
            Assert.True((await r.Content.ReadFromJsonAsync<JsonElement>()).TryGetProperty("errors", out _));
    }
}
