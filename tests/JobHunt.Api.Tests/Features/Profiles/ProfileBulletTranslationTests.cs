using System.Text.Json;
using JobHunt.Api.Tests.Features.Applications;

namespace JobHunt.Api.Tests.Features.Profiles;

public class ProfileBulletTranslationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string SetTranslation = """
        mutation ($bulletId: Int!, $language: Language!, $text: String!, $isMachine: Boolean! = false) {
          setProfileBulletTranslation(input: { bulletId: $bulletId, language: $language, text: $text, isMachine: $isMachine }) {
            profileBulletTranslation { id language text sourceText isMachine isStale }
            errors { __typename }
          }
        }
        """;

    private const string DeleteTranslation = """
        mutation ($id: Int!) {
          deleteProfileBulletTranslation(input: { id: $id }) { id errors { __typename } }
        }
        """;

    internal static async Task<int> AddBulletAsync(HttpClient client, string text = "Built things")
    {
        var experienceId = (await ProfileEntryTests.AddExperienceAsync(client, "Translate Corp")).GetProperty("id").GetInt32();
        var data = await client.GraphQLAsync("""
            mutation ($id: Int!, $text: String!) {
              addProfileBullet(input: { kind: EXPERIENCE, entryId: $id, text: $text }) { profileBullet { id } }
            }
            """, new { id = experienceId, text });
        return data.GetProperty("addProfileBullet").GetProperty("profileBullet").GetProperty("id").GetInt32();
    }

    internal static async Task<JsonElement> SetAsync(HttpClient client, int bulletId, string text,
        string language = "NORWEGIAN", bool? isMachine = null) =>
        (await client.GraphQLAsync(SetTranslation, new { bulletId, language, text, isMachine = isMachine ?? false }))
            .GetProperty("setProfileBulletTranslation");

    private static async Task<JsonElement> TranslationsAsync(HttpClient client, int bulletId)
    {
        var profile = (await client.GraphQLAsync("""
            { profile { experiences { bullets { id text translations { id language text isStale } } } } }
            """)).GetProperty("profile");
        return profile.GetProperty("experiences").EnumerateArray()
            .SelectMany(e => e.GetProperty("bullets").EnumerateArray())
            .Single(b => b.GetProperty("id").GetInt32() == bulletId)
            .GetProperty("translations");
    }

    [Fact]
    public async Task Set_Creates()
    {
        var client = await factory.GetOwnerClientAsync();
        var bulletId = await AddBulletAsync(client, "Built things");

        var translation = (await SetAsync(client, bulletId, "  Bygde ting  ")).GetProperty("profileBulletTranslation");

        Assert.Equal("NORWEGIAN", translation.GetProperty("language").GetString());
        Assert.Equal("Bygde ting", translation.GetProperty("text").GetString());
        Assert.Equal("Built things", translation.GetProperty("sourceText").GetString());
        Assert.False(translation.GetProperty("isMachine").GetBoolean());
        Assert.False(translation.GetProperty("isStale").GetBoolean());
        Assert.Equal(1, (await TranslationsAsync(client, bulletId)).GetArrayLength());
    }

    [Fact]
    public async Task Set_Twice_ReplacesAndKeepsId()
    {
        var client = await factory.GetOwnerClientAsync();
        var bulletId = await AddBulletAsync(client);
        var first = (await SetAsync(client, bulletId, "En")).GetProperty("profileBulletTranslation");

        var second = (await SetAsync(client, bulletId, "To")).GetProperty("profileBulletTranslation");

        Assert.Equal(first.GetProperty("id").GetInt32(), second.GetProperty("id").GetInt32());
        var listed = await TranslationsAsync(client, bulletId);
        Assert.Equal(["To"], listed.EnumerateArray().Select(t => t.GetProperty("text").GetString()));
    }

    [Fact]
    public async Task Set_ByHand_ClearsMachineFlag()
    {
        var client = await factory.GetOwnerClientAsync();
        var bulletId = await AddBulletAsync(client);

        var machine = (await SetAsync(client, bulletId, "Maskin", isMachine: true)).GetProperty("profileBulletTranslation");
        var byHand = (await SetAsync(client, bulletId, "Hånd")).GetProperty("profileBulletTranslation");

        Assert.True(machine.GetProperty("isMachine").GetBoolean());
        Assert.False(byHand.GetProperty("isMachine").GetBoolean());
    }

    [Fact]
    public async Task Set_SameLanguageAsContent_ReturnsInvalidProfileError()
    {
        var client = await factory.GetOwnerClientAsync();
        var bulletId = await AddBulletAsync(client);

        var payload = await SetAsync(client, bulletId, "Same", "ENGLISH");

        Assert.Equal("InvalidProfileError", ApplicationTests.ErrorType(payload));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Set_BlankText_ReturnsInvalidProfileError(string text)
    {
        var client = await factory.GetOwnerClientAsync();
        var bulletId = await AddBulletAsync(client);

        Assert.Equal("InvalidProfileError", ApplicationTests.ErrorType(await SetAsync(client, bulletId, text)));
    }

    [Fact]
    public async Task Set_TextLength_LimitIs500()
    {
        var client = await factory.GetOwnerClientAsync();
        var bulletId = await AddBulletAsync(client);

        var tooLong = await SetAsync(client, bulletId, new string('x', 501));
        var atLimit = await SetAsync(client, bulletId, new string('x', 500));

        Assert.Equal("InvalidProfileError", ApplicationTests.ErrorType(tooLong));
        Assert.Equal(JsonValueKind.Null, atLimit.GetProperty("errors").ValueKind);
    }

    [Fact]
    public async Task Set_UnknownOrForeignBullet_ReturnsNotFound()
    {
        var owner = await factory.GetOwnerClientAsync();
        var guest = await factory.GetGuestClientAsync();
        var bulletId = await AddBulletAsync(owner);

        var unknown = await SetAsync(owner, 999_999, "X");
        var foreign = await SetAsync(guest, bulletId, "X");

        Assert.Equal("ProfileEntryNotFoundError", ApplicationTests.ErrorType(unknown));
        Assert.Equal("ProfileEntryNotFoundError", ApplicationTests.ErrorType(foreign));
    }

    [Fact]
    public async Task Delete_RemovesTranslation_UnknownOrForeignReturnsNotFound()
    {
        var owner = await factory.GetOwnerClientAsync();
        var guest = await factory.GetGuestClientAsync();
        var bulletId = await AddBulletAsync(owner);
        var id = (await SetAsync(owner, bulletId, "X")).GetProperty("profileBulletTranslation").GetProperty("id").GetInt32();

        var foreign = (await guest.GraphQLAsync(DeleteTranslation, new { id })).GetProperty("deleteProfileBulletTranslation");
        var deleted = (await owner.GraphQLAsync(DeleteTranslation, new { id })).GetProperty("deleteProfileBulletTranslation");
        var again = (await owner.GraphQLAsync(DeleteTranslation, new { id })).GetProperty("deleteProfileBulletTranslation");

        Assert.Equal("ProfileEntryNotFoundError", ApplicationTests.ErrorType(foreign));
        Assert.Equal(id, deleted.GetProperty("id").GetInt32());
        Assert.Equal("ProfileEntryNotFoundError", ApplicationTests.ErrorType(again));
        Assert.Equal(0, (await TranslationsAsync(owner, bulletId)).GetArrayLength());
    }

    [Fact]
    public async Task EditingBullet_MakesTranslationStale_UntilSetAgain()
    {
        var client = await factory.GetOwnerClientAsync();
        var bulletId = await AddBulletAsync(client, "Old text");
        await SetAsync(client, bulletId, "Gammel tekst");

        var edit = (await client.GraphQLAsync("""
            mutation ($id: Int!) {
              updateProfileBullet(input: { id: $id, text: "New text" }) {
                profileBullet { translations { text isStale } }
              }
            }
            """, new { id = bulletId })).GetProperty("updateProfileBullet").GetProperty("profileBullet").GetProperty("translations");
        var stale = (await TranslationsAsync(client, bulletId))[0];
        var refreshed = (await SetAsync(client, bulletId, "Ny tekst")).GetProperty("profileBulletTranslation");

        Assert.True(edit[0].GetProperty("isStale").GetBoolean());
        Assert.Equal("Gammel tekst", stale.GetProperty("text").GetString());
        Assert.True(stale.GetProperty("isStale").GetBoolean());
        Assert.False(refreshed.GetProperty("isStale").GetBoolean());
    }

    [Fact]
    public async Task DeletingBulletOrEntry_DeletesTranslation()
    {
        var client = await factory.GetOwnerClientAsync();
        var bulletId = await AddBulletAsync(client);
        var translationId = (await SetAsync(client, bulletId, "X")).GetProperty("profileBulletTranslation").GetProperty("id").GetInt32();
        await client.GraphQLAsync("mutation ($id: Int!) { deleteProfileBullet(input: { id: $id }) { id } }", new { id = bulletId });

        var afterBullet = (await client.GraphQLAsync(DeleteTranslation, new { id = translationId }))
            .GetProperty("deleteProfileBulletTranslation");

        Assert.Equal("ProfileEntryNotFoundError", ApplicationTests.ErrorType(afterBullet));

        var entryBullet = await AddBulletAsync(client);
        var entryTranslationId = (await SetAsync(client, entryBullet, "Y")).GetProperty("profileBulletTranslation").GetProperty("id").GetInt32();
        var experienceId = (await client.GraphQLAsync("{ profile { experiences { id bullets { id } } } }"))
            .GetProperty("profile").GetProperty("experiences").EnumerateArray()
            .Single(e => e.GetProperty("bullets").EnumerateArray().Any(b => b.GetProperty("id").GetInt32() == entryBullet))
            .GetProperty("id").GetInt32();
        await client.GraphQLAsync("mutation ($id: Int!) { deleteProfileExperience(input: { id: $id }) { id } }", new { id = experienceId });

        var afterEntry = (await client.GraphQLAsync(DeleteTranslation, new { id = entryTranslationId }))
            .GetProperty("deleteProfileBulletTranslation");

        Assert.Equal("ProfileEntryNotFoundError", ApplicationTests.ErrorType(afterEntry));
    }

    [Fact]
    public async Task MovingBullet_LeavesTranslationsUnchanged()
    {
        var client = await factory.GetOwnerClientAsync();
        var first = await AddBulletAsync(client, "First");
        var experienceId = (await client.GraphQLAsync("{ profile { experiences { id bullets { id } } } }"))
            .GetProperty("profile").GetProperty("experiences").EnumerateArray()
            .Single(e => e.GetProperty("bullets").EnumerateArray().Any(b => b.GetProperty("id").GetInt32() == first))
            .GetProperty("id").GetInt32();
        var second = (await client.GraphQLAsync("""
            mutation ($id: Int!) {
              addProfileBullet(input: { kind: EXPERIENCE, entryId: $id, text: "Second" }) { profileBullet { id } }
            }
            """, new { id = experienceId })).GetProperty("addProfileBullet").GetProperty("profileBullet").GetProperty("id").GetInt32();
        await SetAsync(client, first, "Første");

        var moved = (await client.GraphQLAsync("""
            mutation ($id: Int!) {
              moveProfileBullet(input: { id: $id, position: 1 }) { bullets { id translations { text isStale } } }
            }
            """, new { id = first })).GetProperty("moveProfileBullet").GetProperty("bullets");

        Assert.Equal([second, first], moved.EnumerateArray().Select(b => b.GetProperty("id").GetInt32()));
        var translations = moved[1].GetProperty("translations");
        Assert.Equal(["Første"], translations.EnumerateArray().Select(t => t.GetProperty("text").GetString()));
        Assert.False(translations[0].GetProperty("isStale").GetBoolean());
    }
}
