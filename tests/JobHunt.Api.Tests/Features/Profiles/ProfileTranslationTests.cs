using System.Text.Json;
using JobHunt.Api.Tests.Features.Applications;

namespace JobHunt.Api.Tests.Features.Profiles;

// Tests in a class run one at a time on one owner profile, so each sets the source text it needs.
public class ProfileTranslationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string SetTranslation = """
        mutation ($language: Language!, $headline: String, $summary: String) {
          setProfileTranslation(input: { language: $language, headline: $headline, summary: $summary }) {
            profileTranslation { language headline summary sourceHeadline sourceSummary isHeadlineStale isSummaryStale isMachine }
            errors { __typename }
          }
        }
        """;

    private const string DeleteTranslation = """
        mutation ($language: Language!) {
          deleteProfileTranslation(input: { language: $language }) { language errors { __typename } }
        }
        """;

    // updateProfile is a full replace, so everything but the two fields under test is left out.
    private static Task SetSourceAsync(HttpClient client, string? headline, string? summary) =>
        client.GraphQLAsync("""
            mutation ($headline: String, $summary: String) {
              updateProfile(input: { headline: $headline, summary: $summary, hobbies: [], links: [] }) { profile { id } }
            }
            """, new { headline, summary });

    private static async Task<JsonElement> SetAsync(HttpClient client, string language, string? headline, string? summary) =>
        (await client.GraphQLAsync(SetTranslation, new { language, headline, summary })).GetProperty("setProfileTranslation");

    private static async Task<JsonElement> ListedAsync(HttpClient client) =>
        (await client.GraphQLAsync("{ profile { translations { language headline summary } } }"))
            .GetProperty("profile").GetProperty("translations");

    [Fact]
    public async Task Set_TranslatesBoth_NeitherStale()
    {
        var client = await factory.GetOwnerClientAsync();
        await SetSourceAsync(client, "Frontend dev", "Likes Vue");

        var translation = (await SetAsync(client, "NORWEGIAN", "  Frontend-utvikler ", "Liker Vue"))
            .GetProperty("profileTranslation");

        Assert.Equal("Frontend-utvikler", translation.GetProperty("headline").GetString());
        Assert.Equal("Liker Vue", translation.GetProperty("summary").GetString());
        Assert.Equal("Frontend dev", translation.GetProperty("sourceHeadline").GetString());
        Assert.False(translation.GetProperty("isHeadlineStale").GetBoolean());
        Assert.False(translation.GetProperty("isSummaryStale").GetBoolean());
        Assert.False(translation.GetProperty("isMachine").GetBoolean());
        Assert.Single((await ListedAsync(client)).EnumerateArray());
    }

    [Fact]
    public async Task Set_Again_ClearsFieldLeftOut()
    {
        var client = await factory.GetOwnerClientAsync();
        await SetSourceAsync(client, "Frontend dev", "Likes Vue");
        await SetAsync(client, "NORWEGIAN", "Utvikler", "Liker Vue");

        var translation = (await SetAsync(client, "NORWEGIAN", "Utvikler", null)).GetProperty("profileTranslation");

        Assert.Equal(JsonValueKind.Null, translation.GetProperty("summary").ValueKind);
        Assert.False(translation.GetProperty("isSummaryStale").GetBoolean());
        Assert.Single((await ListedAsync(client)).EnumerateArray());
    }

    [Fact]
    public async Task Set_FieldWithoutSource_ReturnsInvalidProfileError()
    {
        var client = await factory.GetOwnerClientAsync();
        await SetSourceAsync(client, "Frontend dev", null);

        var payload = await SetAsync(client, "NORWEGIAN", "Utvikler", "Sammendrag");

        Assert.Equal("InvalidProfileError", ApplicationTests.ErrorType(payload));
    }

    [Fact]
    public async Task Set_NothingGiven_ReturnsInvalidProfileError()
    {
        var client = await factory.GetOwnerClientAsync();

        var payload = await SetAsync(client, "NORWEGIAN", "  ", null);

        Assert.Equal("InvalidProfileError", ApplicationTests.ErrorType(payload));
    }

    [Fact]
    public async Task Set_SameLanguageAsContent_ReturnsInvalidProfileError()
    {
        var client = await factory.GetOwnerClientAsync();
        await SetSourceAsync(client, "Frontend dev", null);

        var payload = await SetAsync(client, "ENGLISH", "Same", null);

        Assert.Equal("InvalidProfileError", ApplicationTests.ErrorType(payload));
    }

    [Fact]
    public async Task EditingProfile_MarksOnlyTheChangedFieldStale()
    {
        var client = await factory.GetOwnerClientAsync();
        await SetSourceAsync(client, "Frontend dev", "Likes Vue");
        await SetAsync(client, "NORWEGIAN", "Utvikler", "Liker Vue");

        await SetSourceAsync(client, "Frontend dev", "Likes Vue and Nuxt");
        var translation = (await client.GraphQLAsync("{ profile { translations { isHeadlineStale isSummaryStale summary } } }"))
            .GetProperty("profile").GetProperty("translations")[0];

        Assert.False(translation.GetProperty("isHeadlineStale").GetBoolean());
        Assert.True(translation.GetProperty("isSummaryStale").GetBoolean());
        Assert.Equal("Liker Vue", translation.GetProperty("summary").GetString());
    }

    [Fact]
    public async Task Delete_RemovesLanguage_ThenReturnsNotFound()
    {
        var client = await factory.GetOwnerClientAsync();
        await SetSourceAsync(client, "Frontend dev", null);
        await SetAsync(client, "NORWEGIAN", "Utvikler", null);

        var deleted = (await client.GraphQLAsync(DeleteTranslation, new { language = "NORWEGIAN" }))
            .GetProperty("deleteProfileTranslation");
        var again = (await client.GraphQLAsync(DeleteTranslation, new { language = "NORWEGIAN" }))
            .GetProperty("deleteProfileTranslation");

        Assert.Equal("NORWEGIAN", deleted.GetProperty("language").GetString());
        Assert.Equal("ProfileEntryNotFoundError", ApplicationTests.ErrorType(again));
        Assert.Empty((await ListedAsync(client)).EnumerateArray());
    }
}
