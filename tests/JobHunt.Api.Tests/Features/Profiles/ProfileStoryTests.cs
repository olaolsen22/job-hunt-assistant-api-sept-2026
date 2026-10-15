using System.Text.Json;

namespace JobHunt.Api.Tests.Features.Profiles;

public class ProfileStoryTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Update = """
        mutation ($origin: String, $motivation: String) {
          updateProfileStory(input: { origin: $origin, upbringing: "  ", careerJourney: "From design to code",
                                      motivation: $motivation, values: null }) {
            profile { story { origin upbringing careerJourney motivation values } }
            errors { __typename }
          }
        }
        """;

    private static async Task<JsonElement> UpdateAsync(HttpClient client, string? origin, string? motivation = null) =>
        (await client.GraphQLAsync(Update, new { origin, motivation })).GetProperty("updateProfileStory");

    [Fact]
    public async Task Update_TrimsAndClearsBlankSections()
    {
        var client = await factory.GetOwnerClientAsync();

        var story = (await UpdateAsync(client, "  Grew up in Lagos  ", "Building tools people use"))
            .GetProperty("profile").GetProperty("story");

        Assert.Equal("Grew up in Lagos", story.GetProperty("origin").GetString());
        Assert.Equal(JsonValueKind.Null, story.GetProperty("upbringing").ValueKind);
        Assert.Equal("From design to code", story.GetProperty("careerJourney").GetString());
        Assert.Equal(JsonValueKind.Null, story.GetProperty("values").ValueKind);
    }

    [Fact]
    public async Task Update_TooLong_IsRejected()
    {
        var client = await factory.GetOwnerClientAsync();

        var payload = await UpdateAsync(client, new string('a', 2001));

        Assert.Equal("InvalidProfileError", payload.GetProperty("errors")[0].GetProperty("__typename").GetString());
    }

    [Fact]
    public async Task Story_IsPerUser_AndClearedWithStorySection()
    {
        var owner = await factory.GetOwnerClientAsync();
        var guest = await factory.GetGuestClientAsync();
        await UpdateAsync(owner, "Owner origin");
        await UpdateAsync(guest, "Guest origin");

        var cleared = await guest.GraphQLAsync("""
            mutation { clearProfileSections(input: { sections: [STORY] }) { profile { story { origin careerJourney } } } }
            """);

        var guestStory = cleared.GetProperty("clearProfileSections").GetProperty("profile").GetProperty("story");
        Assert.Equal(JsonValueKind.Null, guestStory.GetProperty("origin").ValueKind);
        Assert.Equal(JsonValueKind.Null, guestStory.GetProperty("careerJourney").ValueKind);
        var ownerStory = (await owner.GraphQLAsync("{ profile { story { origin } } }")).GetProperty("profile").GetProperty("story");
        Assert.Equal("Owner origin", ownerStory.GetProperty("origin").GetString());
    }
}
