using System.Text.Json;
using JobHunt.Api.Data;
using JobHunt.Api.Features.Profiles;
using JobHunt.Api.Tests.Features.Applications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace JobHunt.Api.Tests.Features.Profiles;

public class ProfileSkillWordingTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Suggest = """
        mutation { suggestProfileSkillWording(input: {}) {
          result { status error wording { bullets { id text } headline summary } } errors { __typename }
        } }
        """;

    private const string Apply = """
        mutation ($bullets: [ProfileBulletTextInput!]!, $headline: String, $summary: String) {
          applyProfileSkillWording(input: { bullets: $bullets, headline: $headline, summary: $summary }) {
            profile { headline summary experiences { bullets { id text } } } errors { __typename }
          }
        }
        """;

    private static async Task<int> AddBulletAsync(HttpClient client, string text)
    {
        var experience = await ProfileEntryTests.AddExperienceAsync(client, employer: $"Wording {Guid.NewGuid()}");
        return (await client.GraphQLAsync("""
            mutation ($entryId: Int!, $text: String!) {
              addProfileBullet(input: { kind: EXPERIENCE, entryId: $entryId, text: $text }) { profileBullet { id } }
            }
            """, new { entryId = experience.GetProperty("id").GetInt32(), text }))
            .GetProperty("addProfileBullet").GetProperty("profileBullet").GetProperty("id").GetInt32();
    }

    private static Task SetBasicsAsync(HttpClient client, string headline, string summary) =>
        client.GraphQLAsync("""
            mutation ($headline: String, $summary: String) {
              updateProfile(input: { headline: $headline, summary: $summary, hobbies: [], links: [] }) { profile { id } }
            }
            """, new { headline, summary });

    private static async Task<string?> BulletTextAsync(HttpClient client, int id) =>
        (await client.GraphQLAsync("{ profile { experiences { bullets { id text } } } }"))
        .GetProperty("profile").GetProperty("experiences").EnumerateArray()
        .SelectMany(e => e.GetProperty("bullets").EnumerateArray())
        .Where(b => b.GetProperty("id").GetInt32() == id).Select(b => b.GetProperty("text").GetString()).SingleOrDefault();

    [Fact]
    public async Task Suggest_SendsOwnProfile_KeepsOnlyRealChanges_AndSavesNothing()
    {
        var client = await factory.GetGuestClientAsync();
        await SetBasicsAsync(client, "Vue.js developer", "I like **Vue.js**.");
        var changed = await AddBulletAsync(client, "Built UI in Vue.js");
        var same = await AddBulletAsync(client, "Wrote tests");
        var foreign = await AddBulletAsync(await factory.GetSecondOwnerClientAsync(), "Foreign wording bullet");
        factory.Llm.Respond = () => new ChatResponse(new ChatMessage(ChatRole.Assistant, $$"""
            {"bullets":[{"id":{{changed}},"text":"Built UI in Vue"},{"id":{{same}},"text":"Wrote tests"},
                        {"id":{{foreign}},"text":"Hijacked"},{"id":{{changed}},"text":"Duplicate"}],
             "headline":"Vue developer","summary":"I like **Vue.js**."}
            """));

        var result = (await client.GraphQLAsync(Suggest)).GetProperty("suggestProfileSkillWording").GetProperty("result");

        Assert.Equal("SUCCEEDED", result.GetProperty("status").GetString());
        var wording = result.GetProperty("wording");
        var bullet = Assert.Single(wording.GetProperty("bullets").EnumerateArray());
        Assert.Equal(changed, bullet.GetProperty("id").GetInt32());
        Assert.Equal("Built UI in Vue", bullet.GetProperty("text").GetString());
        Assert.Equal("Vue developer", wording.GetProperty("headline").GetString());
        Assert.Equal(JsonValueKind.Null, wording.GetProperty("summary").ValueKind);
        Assert.Equal(ProfileSkillWordingPromptQueries.Instructions, factory.Llm.LastMessages[0].Text);
        Assert.Contains($"{changed}: Built UI in Vue.js", factory.Llm.LastMessages[1].Text);
        Assert.DoesNotContain("Foreign wording bullet", factory.Llm.LastMessages[1].Text);
        Assert.Equal("Built UI in Vue.js", await BulletTextAsync(client, changed));
        using var scope = factory.Services.CreateScope();
        var usage = await scope.ServiceProvider.GetRequiredService<AppDbContext>().LlmUsages
            .IgnoreQueryFilters().OrderByDescending(u => u.Id).FirstAsync();
        Assert.Equal(ProfileSkillWordingSuggester.Operation, usage.Operation);
    }

    [Fact]
    public async Task Suggest_UnreadableAnswer_Fails()
    {
        var client = await factory.GetOwnerClientAsync();
        factory.Llm.Respond = () => new ChatResponse(new ChatMessage(ChatRole.Assistant, "no"));

        var result = (await client.GraphQLAsync(Suggest)).GetProperty("suggestProfileSkillWording").GetProperty("result");

        Assert.Equal("FAILED", result.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Apply_UpdatesBulletsAndHeadline_LeavingNullFieldsAlone()
    {
        var client = await factory.GetOwnerClientAsync();
        await SetBasicsAsync(client, "Nuxt.js developer", "Keep this summary");
        var id = await AddBulletAsync(client, "Used C#/.NET");

        var profile = (await client.GraphQLAsync(Apply, new
        {
            bullets = new[] { new { id, text = "Used C# and .NET" } }, headline = "Nuxt developer", summary = (string?)null,
        })).GetProperty("applyProfileSkillWording").GetProperty("profile");

        Assert.Equal("Nuxt developer", profile.GetProperty("headline").GetString());
        Assert.Equal("Keep this summary", profile.GetProperty("summary").GetString());
        Assert.Equal("Used C# and .NET", await BulletTextAsync(client, id));
    }

    [Fact]
    public async Task Apply_ForeignOrInvalid_ChangesNothing()
    {
        var owner = await factory.GetOwnerClientAsync();
        var other = await factory.GetSecondOwnerClientAsync();
        var mine = await AddBulletAsync(other, "Mine stays");
        var theirs = await AddBulletAsync(owner, "Theirs stays");

        var foreign = await other.GraphQLAsync(Apply, new
        {
            bullets = new[] { new { id = mine, text = "Changed" }, new { id = theirs, text = "Changed" } },
        });
        var blank = await other.GraphQLAsync(Apply, new { bullets = new[] { new { id = mine, text = " " } } });

        Assert.Equal("ProfileEntryNotFoundError", ApplicationTests.ErrorType(foreign.GetProperty("applyProfileSkillWording")));
        Assert.Equal("InvalidProfileError", ApplicationTests.ErrorType(blank.GetProperty("applyProfileSkillWording")));
        Assert.Equal("Mine stays", await BulletTextAsync(other, mine));
        Assert.Equal("Theirs stays", await BulletTextAsync(owner, theirs));
    }

    [Fact]
    public async Task Prompt_IncludesInstructionsAndBullets()
    {
        var client = await factory.GetOwnerClientAsync();
        var id = await AddBulletAsync(client, "Prompted bullet");

        var prompt = (await client.GraphQLAsync("{ profileSkillWordingPrompt { instructions } }"))
            .GetProperty("profileSkillWordingPrompt").GetProperty("instructions").GetString();

        Assert.StartsWith(ProfileSkillWordingPromptQueries.Instructions, prompt);
        Assert.Contains($"{id}: Prompted bullet", prompt);
    }
}
