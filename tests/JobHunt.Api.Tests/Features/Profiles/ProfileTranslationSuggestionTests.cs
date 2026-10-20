using System.Text.Json;
using JobHunt.Api.Data;
using JobHunt.Api.Features.Listings;
using JobHunt.Api.Features.Profiles;
using JobHunt.Api.Tests.Features.Applications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace JobHunt.Api.Tests.Features.Profiles;

// Tests in a class share one guest profile, so each sets the source text and translations it needs.
public class ProfileTranslationSuggestionTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Suggest = """
        mutation ($language: Language!) {
          suggestProfileTranslation(input: { language: $language }) {
            result { status error translation { bullets { id text } headline summary } } errors { __typename }
          }
        }
        """;

    private const string Apply = """
        mutation ($bullets: [ProfileBulletTextInput!]!, $headline: String, $summary: String) {
          applyProfileTranslation(input: { language: NORWEGIAN, bullets: $bullets, headline: $headline, summary: $summary }) {
            profile {
              translations { language headline summary sourceHeadline sourceSummary isMachine }
              experiences { bullets { id translations { language text sourceText isMachine isStale } } }
            }
            errors { __typename }
          }
        }
        """;

    private static async Task<int> AddBulletAsync(HttpClient client, string text)
    {
        var experience = await ProfileEntryTests.AddExperienceAsync(client, employer: $"Translation {Guid.NewGuid()}");
        return (await client.GraphQLAsync("""
            mutation ($entryId: Int!, $text: String!) {
              addProfileBullet(input: { kind: EXPERIENCE, entryId: $entryId, text: $text }) { profileBullet { id } }
            }
            """, new { entryId = experience.GetProperty("id").GetInt32(), text }))
            .GetProperty("addProfileBullet").GetProperty("profileBullet").GetProperty("id").GetInt32();
    }

    private static Task TranslateBulletAsync(HttpClient client, int bulletId, string text) =>
        client.GraphQLAsync("""
            mutation ($bulletId: Int!, $text: String!) {
              setProfileBulletTranslation(input: { bulletId: $bulletId, language: NORWEGIAN, text: $text }) { profileBulletTranslation { id } }
            }
            """, new { bulletId, text });

    // Starts from source text with no headline/summary translation, whatever an earlier test left.
    private static async Task SetSourceAsync(HttpClient client, string? headline, string? summary)
    {
        await client.GraphQLAsync("mutation { deleteProfileTranslation(input: { language: NORWEGIAN }) { language } }");
        await client.GraphQLAsync("""
            mutation ($headline: String, $summary: String) {
              updateProfile(input: { headline: $headline, summary: $summary, hobbies: [], links: [] }) { profile { id } }
            }
            """, new { headline, summary });
    }

    private static Task AddSampleAsync(HttpClient client, string title, string text, string language) =>
        client.GraphQLAsync("""
            mutation ($title: String!, $text: String!, $language: Language!) {
              addWritingSample(input: { title: $title, text: $text, language: $language, kind: OTHER }) { writingSample { id } }
            }
            """, new { title, text, language });

    private static void Respond(ApiFactory factory, string json) =>
        factory.Llm.Respond = () => new ChatResponse(new ChatMessage(ChatRole.Assistant, json));

    private static async Task<JsonElement> SuggestAsync(HttpClient client, string language = "NORWEGIAN") =>
        (await client.GraphQLAsync(Suggest, new { language })).GetProperty("suggestProfileTranslation");

    private static async Task<JsonElement> ApplyAsync(HttpClient client, object[] bullets, string? headline = null, string? summary = null) =>
        (await client.GraphQLAsync(Apply, new { bullets, headline, summary })).GetProperty("applyProfileTranslation");

    private static JsonElement? BulletTranslation(JsonElement profile, int id) =>
        profile.GetProperty("experiences").EnumerateArray().SelectMany(e => e.GetProperty("bullets").EnumerateArray())
            .Single(b => b.GetProperty("id").GetInt32() == id).GetProperty("translations").EnumerateArray()
            .Cast<JsonElement?>().SingleOrDefault();

    [Fact]
    public async Task Suggest_SendsSamplesAndStates_KeepsOnlyMissingAndStale_AndSavesNothing()
    {
        var client = await factory.GetGuestClientAsync();
        await SetSourceAsync(client, "Frontend developer", "I build **Vue** apps.");
        var missing = await AddBulletAsync(client, "Built the admin UI");
        var stale = await AddBulletAsync(client, "Wrote tests");
        await TranslateBulletAsync(client, stale, "Skrev tester");
        await client.GraphQLAsync("""
            mutation ($id: Int!) { updateProfileBullet(input: { id: $id, text: "Wrote unit tests" }) { profileBullet { id } } }
            """, new { id = stale });
        var current = await AddBulletAsync(client, "Led code reviews");
        await TranslateBulletAsync(client, current, "Ledet kodegjennomganger");
        var foreign = await AddBulletAsync(await factory.GetSecondOwnerClientAsync(), "Foreign translation bullet");
        await AddSampleAsync(client, "Søknad", "Jeg brenner for brukervennlige løsninger.", "NORWEGIAN");
        await AddSampleAsync(client, "Letter", "English sample text.", "ENGLISH");
        Respond(factory, $$"""
            {"headline":" Frontendutvikler ","summary":"Jeg lager **Vue**-apper.","bullets":[
              {"id":{{missing}},"text":"Bygde admin-grensesnittet"},{"id":{{missing}},"text":"Duplikat"},
              {"id":{{stale}},"text":"Skrev enhetstester"},{"id":{{current}},"text":"Overskrevet"},
              {"id":{{foreign}},"text":"Fremmed"},{"id":999999,"text":"Ukjent"}]}
            """);

        var result = (await SuggestAsync(client)).GetProperty("result");

        Assert.Equal("SUCCEEDED", result.GetProperty("status").GetString());
        var translation = result.GetProperty("translation");
        Assert.Equal("Frontendutvikler", translation.GetProperty("headline").GetString());
        Assert.Equal("Jeg lager **Vue**-apper.", translation.GetProperty("summary").GetString());
        Assert.Equal([(missing, "Bygde admin-grensesnittet"), (stale, "Skrev enhetstester")],
            translation.GetProperty("bullets").EnumerateArray()
                .Select(b => (b.GetProperty("id").GetInt32(), b.GetProperty("text").GetString()!)).ToArray());
        Assert.Equal(ProfileTranslationPromptQueries.Instructions(Language.Norwegian, external: false), factory.Llm.LastMessages[0].Text);
        var content = factory.Llm.LastMessages[1].Text;
        Assert.Contains("Jeg brenner for brukervennlige løsninger.", content);
        Assert.DoesNotContain("English sample text.", content);
        Assert.Contains("Ledet kodegjennomganger", content);
        Assert.Contains("\"stale\"", content);
        Assert.DoesNotContain("Foreign translation bullet", content);

        var profile = (await client.GraphQLAsync("""
            { profile { translations { language } experiences { bullets { id translations { language text sourceText isMachine isStale } } } } }
            """)).GetProperty("profile");
        Assert.Empty(profile.GetProperty("translations").EnumerateArray());
        Assert.Null(BulletTranslation(profile, missing));
        Assert.Equal("Ledet kodegjennomganger", BulletTranslation(profile, current)!.Value.GetProperty("text").GetString());
        using var scope = factory.Services.CreateScope();
        var usage = await scope.ServiceProvider.GetRequiredService<AppDbContext>().LlmUsages
            .IgnoreQueryFilters().OrderByDescending(u => u.Id).FirstAsync();
        Assert.Equal(ProfileTranslationSuggester.Operation, usage.Operation);
    }

    [Fact]
    public async Task Suggest_ContentLanguage_FailsWithoutCallingTheModel()
    {
        var client = await factory.GetGuestClientAsync();
        var calls = factory.Llm.Calls;

        var payload = await SuggestAsync(client, "ENGLISH");

        Assert.Equal("InvalidProfileError", payload.GetProperty("errors")[0].GetProperty("__typename").GetString());
        Assert.Equal(calls, factory.Llm.Calls);
    }

    [Fact]
    public async Task Suggest_UnreadableAnswer_Fails()
    {
        var client = await factory.GetGuestClientAsync();
        Respond(factory, "nei");

        var result = (await SuggestAsync(client)).GetProperty("result");

        Assert.Equal("FAILED", result.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("translation").ValueKind);
    }

    [Fact]
    public async Task Prompt_IsExternal_WithProfile()
    {
        var client = await factory.GetGuestClientAsync();
        await AddBulletAsync(client, "Prompted translation bullet");

        var prompt = (await client.GraphQLAsync("{ profileTranslationPrompt(language: NORWEGIAN) { version instructions } }"))
            .GetProperty("profileTranslationPrompt");

        Assert.Equal(ProfileTranslationPromptQueries.Version, prompt.GetProperty("version").GetInt32());
        var text = prompt.GetProperty("instructions").GetString();
        Assert.StartsWith(ProfileTranslationPromptQueries.Instructions(Language.Norwegian, external: true), text);
        Assert.Contains("```json", text);
        Assert.Contains("Prompted translation bullet", text);
    }

    [Fact]
    public async Task Apply_SavesMachineTranslations_AndKeepsAFieldLeftOut()
    {
        var client = await factory.GetGuestClientAsync();
        await SetSourceAsync(client, "Developer", "Summary");
        await client.GraphQLAsync("""
            mutation { setProfileTranslation(input: { language: NORWEGIAN, headline: "Utvikler", summary: "Gammelt" }) { profileTranslation { id } } }
            """);
        var added = await AddBulletAsync(client, "Shipped features");
        var updated = await AddBulletAsync(client, "Fixed bugs");
        await TranslateBulletAsync(client, updated, "Fikset");

        var payload = await ApplyAsync(client,
            [new { id = added, text = " Leverte funksjoner " }, new { id = updated, text = "Rettet feil" }], summary: "Nytt sammendrag");

        var profile = payload.GetProperty("profile");
        var translation = profile.GetProperty("translations").EnumerateArray().Single();
        Assert.Equal("Utvikler", translation.GetProperty("headline").GetString());
        Assert.Equal("Nytt sammendrag", translation.GetProperty("summary").GetString());
        Assert.Equal("Summary", translation.GetProperty("sourceSummary").GetString());
        Assert.True(translation.GetProperty("isMachine").GetBoolean());
        var addedTranslation = BulletTranslation(profile, added)!.Value;
        Assert.Equal("Leverte funksjoner", addedTranslation.GetProperty("text").GetString());
        Assert.Equal("Shipped features", addedTranslation.GetProperty("sourceText").GetString());
        Assert.True(addedTranslation.GetProperty("isMachine").GetBoolean());
        Assert.False(addedTranslation.GetProperty("isStale").GetBoolean());
        Assert.Equal("Rettet feil", BulletTranslation(profile, updated)!.Value.GetProperty("text").GetString());
    }

    [Fact]
    public async Task Apply_RejectsRepeatedForeignAndSourcelessInput_SavingNothing()
    {
        var client = await factory.GetGuestClientAsync();
        await SetSourceAsync(client, "Developer", null);
        var own = await AddBulletAsync(client, "Own bullet to translate");
        var foreign = await AddBulletAsync(await factory.GetSecondOwnerClientAsync(), "Foreign bullet to translate");

        var repeated = await ApplyAsync(client, [new { id = own, text = "A" }, new { id = own, text = "B" }]);
        var foreignPayload = await ApplyAsync(client, [new { id = own, text = "Eget" }, new { id = foreign, text = "Fremmed" }]);
        var sourceless = await ApplyAsync(client, [new { id = own, text = "Eget" }], summary: "Sammendrag");
        var english = await client.GraphQLAsync("""
            mutation { applyProfileTranslation(input: { language: ENGLISH, bullets: [] }) { errors { __typename } } }
            """);

        Assert.Equal("InvalidProfileError", repeated.GetProperty("errors")[0].GetProperty("__typename").GetString());
        Assert.Equal("ProfileEntryNotFoundError", foreignPayload.GetProperty("errors")[0].GetProperty("__typename").GetString());
        Assert.Equal("InvalidProfileError", sourceless.GetProperty("errors")[0].GetProperty("__typename").GetString());
        Assert.Equal("InvalidProfileError",
            english.GetProperty("applyProfileTranslation").GetProperty("errors")[0].GetProperty("__typename").GetString());
        var profile = (await client.GraphQLAsync("{ profile { experiences { bullets { id translations { text } } } } }"))
            .GetProperty("profile");
        Assert.Null(BulletTranslation(profile, own));
    }
}
