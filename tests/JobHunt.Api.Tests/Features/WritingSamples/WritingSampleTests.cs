using System.Text.Json;
using JobHunt.Api.Features.Listings;
using JobHunt.Api.Features.WritingSamples;

namespace JobHunt.Api.Tests.Features.WritingSamples;

public class WritingSampleTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Add = """
        mutation ($title: String!, $text: String!) {
          addWritingSample(input: { title: $title, text: $text, language: ENGLISH, kind: COVER_LETTER }) {
            writingSample { id title text language kind }
            errors { __typename }
          }
        }
        """;

    internal static async Task<JsonElement> AddAsync(HttpClient client, string title, string text = "Dear team, ...") =>
        (await client.GraphQLAsync(Add, new { title, text })).GetProperty("addWritingSample");

    private static async Task<List<string>> TitlesAsync(HttpClient client) =>
        [.. (await client.GraphQLAsync("{ writingSamples(first: 50) { nodes { title } } }"))
            .GetProperty("writingSamples").GetProperty("nodes").EnumerateArray()
            .Select(n => n.GetProperty("title").GetString()!)];

    private static async Task DeleteAllAsync(HttpClient client)
    {
        var ids = (await client.GraphQLAsync("{ writingSamples(first: 50) { nodes { id } } }"))
            .GetProperty("writingSamples").GetProperty("nodes").EnumerateArray().Select(n => n.GetProperty("id").GetInt32());
        foreach (var id in ids)
            await client.GraphQLAsync("mutation ($id: Int!) { deleteWritingSample(input: { id: $id }) { id } }", new { id });
    }

    [Fact]
    public async Task Add_Update_Delete()
    {
        var client = await factory.GetOwnerClientAsync();
        var added = (await AddAsync(client, "  Old letter  ", "  Dear Acme,  ")).GetProperty("writingSample");
        Assert.Equal("Old letter", added.GetProperty("title").GetString());
        Assert.Equal("Dear Acme,", added.GetProperty("text").GetString());
        var id = added.GetProperty("id").GetInt32();

        var updated = await client.GraphQLAsync("""
            mutation ($id: Int!) {
              updateWritingSample(input: { id: $id, title: "Blog post", text: "Hei!", language: NORWEGIAN, kind: OTHER }) {
                writingSample { title language kind }
              }
            }
            """, new { id });
        var sample = updated.GetProperty("updateWritingSample").GetProperty("writingSample");
        Assert.Equal("NORWEGIAN", sample.GetProperty("language").GetString());
        Assert.Equal("OTHER", sample.GetProperty("kind").GetString());

        var deleted = await client.GraphQLAsync("mutation ($id: Int!) { deleteWritingSample(input: { id: $id }) { id } }", new { id });
        Assert.Equal(id, deleted.GetProperty("deleteWritingSample").GetProperty("id").GetInt32());
        Assert.DoesNotContain("Blog post", await TitlesAsync(client));
    }

    [Fact]
    public async Task BlankText_IsRejected()
    {
        var client = await factory.GetOwnerClientAsync();

        var payload = await AddAsync(client, "Empty", "   ");

        Assert.Equal("InvalidWritingSampleError", payload.GetProperty("errors")[0].GetProperty("__typename").GetString());
    }

    [Fact]
    public async Task Samples_ArePerUser()
    {
        var owner = await factory.GetOwnerClientAsync();
        var guest = await factory.GetGuestClientAsync();
        var id = (await AddAsync(owner, "Owner only")).GetProperty("writingSample").GetProperty("id").GetInt32();

        Assert.DoesNotContain("Owner only", await TitlesAsync(guest));
        var update = await guest.GraphQLAsync("""
            mutation ($id: Int!) {
              updateWritingSample(input: { id: $id, title: "Taken", text: "x", language: ENGLISH, kind: OTHER }) { errors { __typename } }
            }
            """, new { id });
        Assert.Equal("WritingSampleNotFoundError",
            update.GetProperty("updateWritingSample").GetProperty("errors")[0].GetProperty("__typename").GetString());
    }

    [Fact]
    public async Task Add_StopsAtTheCap()
    {
        var client = await factory.GetGuestClientAsync();
        await DeleteAllAsync(client);
        for (var i = 0; i < 20; i++) await AddAsync(client, $"Sample {i}");

        var payload = await AddAsync(client, "One too many");

        Assert.Equal("InvalidWritingSampleError", payload.GetProperty("errors")[0].GetProperty("__typename").GetString());
        await DeleteAllAsync(client);
    }

    [Fact]
    public void ForPrompt_PrefersLanguageThenCoverLettersThenNewest_WithinBudget()
    {
        var now = DateTime.UtcNow;
        WritingSample Sample(string title, Language language, WritingSampleKind kind, int ageDays, int length = 10) =>
            new() { Title = title, Text = new string('x', length), Language = language, Kind = kind, UpdatedAt = now.AddDays(-ageDays) };
        List<WritingSample> samples =
        [
            Sample("norwegian", Language.Norwegian, WritingSampleKind.CoverLetter, 0),
            Sample("old letter", Language.English, WritingSampleKind.CoverLetter, 5),
            Sample("other", Language.English, WritingSampleKind.Other, 0),
            Sample("new letter", Language.English, WritingSampleKind.CoverLetter, 1),
            Sample("too big", Language.English, WritingSampleKind.CoverLetter, 0, length: 100),
        ];

        var selected = WritingSample.ForPrompt(samples, Language.English, budget: 35);

        Assert.Equal(["new letter", "old letter", "other"], selected.Select(s => s.Title));
    }
}
