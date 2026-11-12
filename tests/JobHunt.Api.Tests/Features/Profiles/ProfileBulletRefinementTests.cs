using System.Text.Json;
using JobHunt.Api.Data;
using JobHunt.Api.Features.Listings;
using JobHunt.Api.Features.Profiles;
using JobHunt.Api.Tests.Features.Applications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace JobHunt.Api.Tests.Features.Profiles;

// Tests in a class share one profile per account, so each makes its own entries with unique texts.
public class ProfileBulletRefinementTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Refine = """
        mutation ($bulletId: Int, $kind: ProfileEntryKind, $entryId: Int, $model: String) {
          refineProfileBullets(input: { bulletId: $bulletId, kind: $kind, entryId: $entryId, model: $model }) {
            result { status error bullets { bulletId text note } } errors { __typename }
          }
        }
        """;

    private const string Preview = """
        mutation ($json: String!, $bulletId: Int, $kind: ProfileEntryKind, $entryId: Int) {
          previewProfileBulletRefinement(input: { json: $json, bulletId: $bulletId, kind: $kind, entryId: $entryId }) {
            result { status error bullets { bulletId text note } } errors { __typename }
          }
        }
        """;

    private const string Apply = """
        mutation ($bullets: [ProfileBulletTextInput!]!) {
          applyProfileBulletRefinement(input: { bullets: $bullets }) {
            profile { experiences { bullets { id text position translations { isStale } } } } errors { __typename }
          }
        }
        """;

    private const string Prompt = """
        query ($bulletId: Int, $kind: ProfileEntryKind, $entryId: Int) {
          profileBulletRefinementPrompt(bulletId: $bulletId, kind: $kind, entryId: $entryId) { version instructions }
        }
        """;

    private static async Task<(int Entry, int[] Bullets)> AddEntryAsync(HttpClient client, string[] texts, string[]? skills = null)
    {
        var entry = (await ProfileEntryTests.AddExperienceAsync(client, employer: $"Refine {Guid.NewGuid()}", skills: skills))
            .GetProperty("id").GetInt32();
        var bullets = new List<int>();
        foreach (var text in texts)
            bullets.Add((await client.GraphQLAsync("""
                mutation ($entryId: Int!, $text: String!) {
                  addProfileBullet(input: { kind: EXPERIENCE, entryId: $entryId, text: $text }) { profileBullet { id } }
                }
                """, new { entryId = entry, text }))
                .GetProperty("addProfileBullet").GetProperty("profileBullet").GetProperty("id").GetInt32());
        return (entry, [.. bullets]);
    }

    private void Respond(string json) =>
        factory.Llm.Respond = () => new ChatResponse(new ChatMessage(ChatRole.Assistant, json));

    private static async Task<JsonElement> RefineAsync(HttpClient client, object variables) =>
        (await client.GraphQLAsync(Refine, variables)).GetProperty("refineProfileBullets");

    private static JsonElement[] Bullets(JsonElement payload) =>
        [.. payload.GetProperty("result").GetProperty("bullets").EnumerateArray()];

    private static async Task<string?> BulletTextAsync(HttpClient client, int id) =>
        (await client.GraphQLAsync("{ profile { experiences { bullets { id text } } } }"))
        .GetProperty("profile").GetProperty("experiences").EnumerateArray()
        .SelectMany(e => e.GetProperty("bullets").EnumerateArray())
        .Where(b => b.GetProperty("id").GetInt32() == id).Select(b => b.GetProperty("text").GetString()).SingleOrDefault();

    [Fact]
    public async Task Refine_Entry_KeepsTargetsOnly_AndSavesNothingButUsage()
    {
        var client = await factory.GetGuestClientAsync();
        var (entry, bullets) = await AddEntryAsync(client, ["Was responsible for making the checkout page work with keyboard", "Wrote tests"]);
        var (_, other) = await AddEntryAsync(client, ["Other entry bullet"]);
        Respond($$"""
            {"bullets":[
              {"id":{{bullets[0]}},"text":"Made the checkout page fully usable by keyboard","note":null},
              {"id":{{bullets[1]}},"text":"Wrote tests","note":null},
              {"id":{{other[0]}},"text":"Hijacked","note":null}]}
            """);

        var payload = await RefineAsync(client, new { kind = "EXPERIENCE", entryId = entry });

        Assert.Equal("SUCCEEDED", payload.GetProperty("result").GetProperty("status").GetString());
        var only = Assert.Single(Bullets(payload));
        Assert.Equal(bullets[0], only.GetProperty("bulletId").GetInt32());
        Assert.Equal("Made the checkout page fully usable by keyboard", only.GetProperty("text").GetString());
        Assert.Equal("Was responsible for making the checkout page work with keyboard", await BulletTextAsync(client, bullets[0]));
        using var scope = factory.Services.CreateScope();
        var usage = await scope.ServiceProvider.GetRequiredService<AppDbContext>().LlmUsages
            .IgnoreQueryFilters().OrderByDescending(u => u.Id).FirstAsync();
        Assert.Equal(ProfileBulletRefiner.Operation, usage.Operation);
    }

    [Fact]
    public async Task Refine_OneBullet_SendsOthersAsContext_AndDropsSiblingSuggestion()
    {
        var client = await factory.GetGuestClientAsync();
        var (_, bullets) = await AddEntryAsync(client, ["Single target bullet", "Sibling context bullet"]);
        Respond($$"""
            {"bullets":[{"id":{{bullets[0]}},"text":"Refined single target","note":null},
                        {"id":{{bullets[1]}},"text":"Refined sibling","note":null}]}
            """);

        var payload = await RefineAsync(client, new { bulletId = bullets[0] });

        var only = Assert.Single(Bullets(payload));
        Assert.Equal(bullets[0], only.GetProperty("bulletId").GetInt32());
        var content = factory.Llm.LastMessages[1].Text;
        Assert.Contains($"\"id\": {bullets[0]},\n          \"text\": \"Single target bullet\",\n          \"refine\": true", content);
        Assert.Contains($"\"id\": {bullets[1]},\n          \"text\": \"Sibling context bullet\",\n          \"refine\": false", content);
        Assert.Equal(ProfileBulletRefinementPromptQueries.Instructions(Language.English, external: false),
            factory.Llm.LastMessages[0].Text);
    }

    [Fact]
    public async Task Refine_WholeProfile_AnyBulletIsATarget()
    {
        var client = await factory.GetGuestClientAsync();
        var (_, first) = await AddEntryAsync(client, ["Whole profile first"]);
        var (_, second) = await AddEntryAsync(client, ["Whole profile second"]);
        Respond($$"""
            {"bullets":[{"id":{{first[0]}},"text":"Whole profile first, refined"},
                        {"id":{{second[0]}},"text":null,"note":"What was the result?"}]}
            """);

        var bullets = Bullets(await RefineAsync(client, new { }));

        Assert.Equal([first[0], second[0]], bullets.Select(b => b.GetProperty("bulletId").GetInt32()));
        Assert.Equal(JsonValueKind.Null, bullets[1].GetProperty("text").ValueKind);
        Assert.Equal("What was the result?", bullets[1].GetProperty("note").GetString());
    }

    [Fact]
    public async Task Refine_TextAddingFacts_IsDropped_NoteKept()
    {
        var client = await factory.GetGuestClientAsync();
        var (_, bullets) = await AddEntryAsync(client,
            ["Built the booking flow in Vue", "Built the search page", "Set up the admin page", "Kept the old text"],
            skills: ["TypeScript", "React"]);
        await AddEntryAsync(client, ["Sibling built the shop in React"]);
        var longNote = new string('x', 301);
        Respond($$"""
            {"bullets":[
              {"id":{{bullets[0]}},"text":"Built the booking flow in Vue, cutting drop-off by 20%","note":"Did you measure a result?"},
              {"id":{{bullets[1]}},"text":"Built the search page in TypeScript","note":null},
              {"id":{{bullets[2]}},"text":"Set up the admin page in React","note":null},
              {"id":{{bullets[3]}},"text":"Kept the old text","note":"{{longNote}}"}]}
            """);

        var result = Bullets(await RefineAsync(client, new { kind = "EXPERIENCE", entryId = await EntryOfAsync(client, bullets[0]) }));

        var only = Assert.Single(result);
        Assert.Equal(bullets[0], only.GetProperty("bulletId").GetInt32());
        Assert.Equal(JsonValueKind.Null, only.GetProperty("text").ValueKind);
        Assert.Equal("Did you measure a result?", only.GetProperty("note").GetString());
    }

    private static async Task<int> EntryOfAsync(HttpClient client, int bulletId) =>
        (await client.GraphQLAsync("{ profile { experiences { id bullets { id } } } }"))
        .GetProperty("profile").GetProperty("experiences").EnumerateArray()
        .Single(e => e.GetProperty("bullets").EnumerateArray().Any(b => b.GetProperty("id").GetInt32() == bulletId))
        .GetProperty("id").GetInt32();

    [Fact]
    public async Task Refine_UnreadableAnswer_Fails()
    {
        var client = await factory.GetGuestClientAsync();
        var (_, bullets) = await AddEntryAsync(client, ["Unreadable answer bullet"]);
        Respond("no");

        var result = (await RefineAsync(client, new { bulletId = bullets[0] })).GetProperty("result");

        Assert.Equal("FAILED", result.GetProperty("status").GetString());
        Assert.False(string.IsNullOrEmpty(result.GetProperty("error").GetString()));
    }

    [Fact]
    public async Task Refine_InvalidScopes_FailBeforeAnyCall()
    {
        var client = await factory.GetGuestClientAsync();
        var (entry, bullets) = await AddEntryAsync(client, ["Scope bullet"]);
        var empty = (await ProfileEntryTests.AddExperienceAsync(client, employer: $"Empty {Guid.NewGuid()}")).GetProperty("id").GetInt32();
        var (_, foreign) = await AddEntryAsync(await factory.GetSecondOwnerClientAsync(), ["Foreign refine bullet"]);
        var calls = factory.Llm.Calls;

        Assert.Equal("InvalidProfileError", ApplicationTests.ErrorType(await RefineAsync(client, new { bulletId = bullets[0], entryId = entry })));
        Assert.Equal("InvalidProfileError", ApplicationTests.ErrorType(await RefineAsync(client, new { entryId = entry })));
        Assert.Equal("InvalidProfileError", ApplicationTests.ErrorType(await RefineAsync(client, new { kind = "EXPERIENCE", entryId = empty })));
        Assert.Equal("ProfileEntryNotFoundError", ApplicationTests.ErrorType(await RefineAsync(client, new { bulletId = foreign[0] })));
        Assert.Equal("ProfileEntryNotFoundError", ApplicationTests.ErrorType(await RefineAsync(client, new { kind = "PROJECT", entryId = entry })));
        Assert.Equal(calls, factory.Llm.Calls);
    }

    [Fact]
    public async Task Refine_UnknownModel_Fails()
    {
        var client = await factory.GetGuestClientAsync();
        var (_, bullets) = await AddEntryAsync(client, ["Model bullet"]);

        var payload = await RefineAsync(client, new { bulletId = bullets[0], model = "no-such-model" });

        Assert.Equal("UnknownLlmModelError", ApplicationTests.ErrorType(payload));
    }

    [Fact]
    public async Task Prompt_HasRulesSamplesInContentLanguage_AndNoPersonalDetails()
    {
        var client = await factory.GetOwnerClientAsync();
        await client.GraphQLAsync("""
            mutation { updateProfile(input: { email: "refine@example.com", phone: "+47 99887766", hobbies: [], links: [] }) { profile { id } } }
            """);
        await client.GraphQLAsync("""
            mutation { updateProfileAddress(input: { streetAddress: "Refinegata 12", postalCode: "0150" }) { profile { id } } }
            """);
        foreach (var (title, sample, language) in new[] { ("English sample", "I write short, plain sentences.", "ENGLISH"), ("Norsk", "Jeg skriver kort.", "NORWEGIAN") })
            await client.GraphQLAsync("""
                mutation ($title: String!, $text: String!, $language: Language!) {
                  addWritingSample(input: { title: $title, text: $text, language: $language, kind: OTHER }) { writingSample { id } }
                }
                """, new { title, text = sample, language });
        var (_, bullets) = await AddEntryAsync(client, ["Prompted refine bullet"]);

        var prompt = (await client.GraphQLAsync(Prompt, new { bulletId = bullets[0] }))
            .GetProperty("profileBulletRefinementPrompt");
        var text = prompt.GetProperty("instructions").GetString()!;

        Assert.Equal(ProfileBulletRefinementPromptQueries.Version, prompt.GetProperty("version").GetInt32());
        Assert.StartsWith(ProfileBulletRefinementPromptQueries.Instructions(Language.English, external: true), text);
        Assert.Contains("the candidate's own part, and the useful result or scope", text);
        Assert.Contains("one short question in English", text);
        Assert.Contains("Prompted refine bullet", text);
        Assert.Contains("I write short, plain sentences.", text);
        Assert.DoesNotContain("Jeg skriver kort.", text);
        Assert.DoesNotContain("refine@example.com", text);
        Assert.DoesNotContain("99887766", text);
        Assert.DoesNotContain("Refinegata", text);
    }

    [Fact]
    public async Task Prompt_InvalidScope_IsATopLevelError()
    {
        var client = await factory.GetOwnerClientAsync();

        var error = await client.GraphQLErrorAsync(Prompt, new { entryId = 1 });

        Assert.Contains("Refine one bullet", error);
    }

    [Fact]
    public async Task Preview_RunsTheSameChecks()
    {
        var client = await factory.GetOwnerClientAsync();
        var (_, bullets) = await AddEntryAsync(client, ["Pasted bullet about the shop", "Pasted bullet about the cart"]);
        var json = $$"""
            {"bullets":[{"id":{{bullets[0]}},"text":"Pasted bullet about the shop for 3 clients","note":"Who used it?"},
                        {"id":{{bullets[1]}},"text":"Built the cart","note":null}]}
            """;

        var preview = (await client.GraphQLAsync(Preview, new { json, bulletId = bullets[0] }))
            .GetProperty("previewProfileBulletRefinement");
        var broken = (await client.GraphQLAsync(Preview, new { json = "not json", bulletId = bullets[0] }))
            .GetProperty("previewProfileBulletRefinement").GetProperty("result");

        var only = Assert.Single(Bullets(preview));
        Assert.Equal(JsonValueKind.Null, only.GetProperty("text").ValueKind);
        Assert.Equal("Who used it?", only.GetProperty("note").GetString());
        Assert.Equal("FAILED", broken.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Apply_SavesTexts_KeepsIds_AndMarksTranslationsStale()
    {
        var client = await factory.GetOwnerClientAsync();
        var (_, bullets) = await AddEntryAsync(client, ["Apply first", "Apply second"]);
        await client.GraphQLAsync("""
            mutation ($bulletId: Int!) {
              setProfileBulletTranslation(input: { bulletId: $bulletId, language: NORWEGIAN, text: "Bruk først" }) { profileBulletTranslation { id } }
            }
            """, new { bulletId = bullets[0] });

        var payload = (await client.GraphQLAsync(Apply, new
        {
            bullets = new[] { new { id = bullets[0], text = "Applied first, by about 30%" }, new { id = bullets[1], text = " Applied second " } },
        })).GetProperty("applyProfileBulletRefinement");

        var saved = payload.GetProperty("profile").GetProperty("experiences").EnumerateArray()
            .SelectMany(e => e.GetProperty("bullets").EnumerateArray())
            .Where(b => bullets.Contains(b.GetProperty("id").GetInt32())).OrderBy(b => b.GetProperty("position").GetInt32()).ToList();
        Assert.Equal(["Applied first, by about 30%", "Applied second"], saved.Select(b => b.GetProperty("text").GetString()));
        Assert.Equal([0, 1], saved.Select(b => b.GetProperty("position").GetInt32()));
        Assert.True(saved[0].GetProperty("translations")[0].GetProperty("isStale").GetBoolean());
    }

    [Fact]
    public async Task Apply_ForeignDuplicateOrBlank_ChangesNothing()
    {
        var owner = await factory.GetOwnerClientAsync();
        var other = await factory.GetSecondOwnerClientAsync();
        var (_, mine) = await AddEntryAsync(other, ["Mine stays put"]);
        var (_, theirs) = await AddEntryAsync(owner, ["Theirs stays put"]);

        var foreign = await other.GraphQLAsync(Apply, new
        {
            bullets = new[] { new { id = mine[0], text = "Changed" }, new { id = theirs[0], text = "Changed" } },
        });
        var duplicate = await other.GraphQLAsync(Apply, new
        {
            bullets = new[] { new { id = mine[0], text = "Changed" }, new { id = mine[0], text = "Again" } },
        });
        var blank = await other.GraphQLAsync(Apply, new { bullets = new[] { new { id = mine[0], text = " " } } });

        Assert.Equal("ProfileEntryNotFoundError", ApplicationTests.ErrorType(foreign.GetProperty("applyProfileBulletRefinement")));
        Assert.Equal("InvalidProfileError", ApplicationTests.ErrorType(duplicate.GetProperty("applyProfileBulletRefinement")));
        Assert.Equal("InvalidProfileError", ApplicationTests.ErrorType(blank.GetProperty("applyProfileBulletRefinement")));
        Assert.Equal("Mine stays put", await BulletTextAsync(other, mine[0]));
        Assert.Equal("Theirs stays put", await BulletTextAsync(owner, theirs[0]));
    }
}
