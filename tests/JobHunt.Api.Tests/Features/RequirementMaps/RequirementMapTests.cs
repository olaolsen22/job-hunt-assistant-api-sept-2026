using System.Text.Json;
using JobHunt.Api.Data;
using JobHunt.Api.Tests.Features.Profiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace JobHunt.Api.Tests.Features.RequirementMaps;

public class RequirementMapTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Needs = "needs { text kind importance match bulletIds entries { kind id } note }";
    private const string Save = $$"""
        mutation ($listingId: Int!, $needs: [RequirementNeedInput!]!) {
          saveRequirementMap(input: { listingId: $listingId, content: { needs: $needs } }) {
            requirementMap { id createdAt updatedAt content { {{Needs}} } }
            errors { __typename ... on Error { message } }
          }
        }
        """;
    private const string Read = $$"""
        query ($listingId: Int!) { requirementMap(listingId: $listingId) { id createdAt updatedAt content { {{Needs}} } } }
        """;
    private const string Delete = """
        mutation ($listingId: Int!) { deleteRequirementMap(input: { listingId: $listingId }) { listingId errors { __typename } } }
        """;
    private const string Generate = $$"""
        mutation ($listingId: Int!, $model: String) {
          generateRequirementMap(input: { listingId: $listingId, model: $model }) {
            result { status error errorCode content { {{Needs}} } }
            errors { __typename }
          }
        }
        """;
    private const string Preview = $$"""
        mutation ($listingId: Int!, $json: String!) {
          previewRequirementMap(input: { listingId: $listingId, json: $json }) {
            candidate { {{Needs}} }
            errors { __typename ... on Error { message } }
          }
        }
        """;
    private const string Prompt = """
        query ($listingId: Int!) { requirementMapExternalPrompt(listingId: $listingId) { version instructions } }
        """;

    [Fact]
    public async Task Save_StoresNeedsInOrder_AndReplacesOnSecondSave()
    {
        var client = await factory.GetOwnerClientAsync();
        var listingId = await CreateListingAsync(client);
        var (experienceId, bulletId) = await AddEvidenceAsync(client);

        var first = await SaveOkAsync(client, listingId,
            Need(" React and TypeScript ", bulletIds: [bulletId, bulletId]),
            Need("Bachelor's degree", "QUALIFICATION", "PREFERRED", "PARTIAL", entries: [new { kind = "EXPERIENCE", id = experienceId }], note: " Related work "));

        var needs = first.GetProperty("content").GetProperty("needs");
        Assert.Equal(["React and TypeScript", "Bachelor's degree"], needs.EnumerateArray().Select(n => n.GetProperty("text").GetString()));
        Assert.Equal([bulletId], needs[0].GetProperty("bulletIds").EnumerateArray().Select(i => i.GetInt32()));
        Assert.Equal("SKILL", needs[0].GetProperty("kind").GetString());
        Assert.Equal("EVIDENCED", needs[0].GetProperty("match").GetString());
        Assert.Equal(experienceId, needs[1].GetProperty("entries")[0].GetProperty("id").GetInt32());
        Assert.Equal("Related work", needs[1].GetProperty("note").GetString());

        var second = await SaveOkAsync(client, listingId, Need("Only need", match: "GAP"));

        Assert.Equal(first.GetProperty("id").GetInt32(), second.GetProperty("id").GetInt32());
        Assert.Equal(first.GetProperty("createdAt").GetDateTime(), second.GetProperty("createdAt").GetDateTime());
        Assert.True(second.GetProperty("updatedAt").GetDateTime() >= first.GetProperty("updatedAt").GetDateTime());
        var read = (await client.GraphQLAsync(Read, new { listingId })).GetProperty("requirementMap");
        Assert.Equal(["Only need"], read.GetProperty("content").GetProperty("needs").EnumerateArray().Select(n => n.GetProperty("text").GetString()));
    }

    [Fact]
    public async Task Save_InvalidContent_IsRejectedAndStoresNothing()
    {
        var owner = await factory.GetOwnerClientAsync();
        var guest = await factory.GetGuestClientAsync();
        var listingId = await CreateListingAsync(owner);
        var (_, guestBulletId) = await AddEvidenceAsync(guest);
        var invalid = new object[][]
        {
            [],
            [.. Enumerable.Range(0, 31).Select(i => Need($"Need {i}", match: "GAP"))],
            [Need(new string('x', 201), match: "GAP")],
            [Need("   ", match: "GAP")],
            [Need("Note too long", match: "GAP", note: new string('x', 501))],
            [Need("No support")],
            [Need("Another user's bullet", bulletIds: [guestBulletId])],
            [Need("Unknown entry", entries: [new { kind = "PROJECT", id = 999_999 }])],
            [Need("Too many references", bulletIds: [.. Enumerable.Range(1, 11)])],
        };

        foreach (var needs in invalid)
        {
            var payload = (await owner.GraphQLAsync(Save, new { listingId, needs })).GetProperty("saveRequirementMap");
            Assert.Equal("InvalidRequirementMapError", payload.GetProperty("errors")[0].GetProperty("__typename").GetString());
        }
        Assert.Equal(JsonValueKind.Null, (await owner.GraphQLAsync(Read, new { listingId })).GetProperty("requirementMap").ValueKind);
    }

    [Fact]
    public async Task Save_GapDropsEvidence_AndANoteSupportsALanguageNeed()
    {
        var client = await factory.GetOwnerClientAsync();
        var listingId = await CreateListingAsync(client);
        var (_, bulletId) = await AddEvidenceAsync(client);

        var map = await SaveOkAsync(client, listingId,
            Need("Client workshops", "DUTY", match: "GAP", bulletIds: [bulletId]),
            Need("Norwegian B2", "LANGUAGE", note: "Profile: Norwegian, good written and spoken"));

        var needs = map.GetProperty("content").GetProperty("needs");
        Assert.Empty(needs[0].GetProperty("bulletIds").EnumerateArray());
        Assert.Equal("GAP", needs[0].GetProperty("match").GetString());
        Assert.Equal("EVIDENCED", needs[1].GetProperty("match").GetString());
    }

    [Fact]
    public async Task OtherUsersListing_IsNotFoundAndReadsNull()
    {
        var owner = await factory.GetOwnerClientAsync();
        var guest = await factory.GetGuestClientAsync();
        var listingId = await CreateListingAsync(owner);
        await SaveOkAsync(owner, listingId, Need("Owner need", match: "GAP"));

        var save = (await guest.GraphQLAsync(Save, new { listingId, needs = new[] { Need("Guest need", match: "GAP") } }))
            .GetProperty("saveRequirementMap");
        var delete = (await guest.GraphQLAsync(Delete, new { listingId })).GetProperty("deleteRequirementMap");
        var preview = (await guest.GraphQLAsync(Preview, new { listingId, json = MapJson("""{"text":"x","kind":"DUTY","importance":"REQUIRED","match":"GAP"}""") }))
            .GetProperty("previewRequirementMap");

        Assert.Equal("ListingNotFoundError", save.GetProperty("errors")[0].GetProperty("__typename").GetString());
        Assert.Equal("RequirementMapNotFoundError", delete.GetProperty("errors")[0].GetProperty("__typename").GetString());
        Assert.Equal("ListingNotFoundError", preview.GetProperty("errors")[0].GetProperty("__typename").GetString());
        Assert.Equal(JsonValueKind.Null, (await guest.GraphQLAsync(Read, new { listingId })).GetProperty("requirementMap").ValueKind);
        Assert.Contains("was not found", await guest.GraphQLErrorAsync(Prompt, new { listingId }));
        var read = (await owner.GraphQLAsync(Read, new { listingId })).GetProperty("requirementMap");
        Assert.Equal("Owner need", read.GetProperty("content").GetProperty("needs")[0].GetProperty("text").GetString());
    }

    [Fact]
    public async Task SavedReferences_SurviveDeletingTheBullet()
    {
        var client = await factory.GetOwnerClientAsync();
        var listingId = await CreateListingAsync(client);
        var (_, bulletId) = await AddEvidenceAsync(client);
        await SaveOkAsync(client, listingId, Need("React", bulletIds: [bulletId]));

        await client.GraphQLAsync("mutation ($id: Int!) { deleteProfileBullet(input: { id: $id }) { id } }", new { id = bulletId });

        var need = (await client.GraphQLAsync(Read, new { listingId })).GetProperty("requirementMap")
            .GetProperty("content").GetProperty("needs")[0];
        Assert.Equal([bulletId], need.GetProperty("bulletIds").EnumerateArray().Select(i => i.GetInt32()));
        Assert.Equal("EVIDENCED", need.GetProperty("match").GetString());
    }

    [Fact]
    public async Task Delete_RemovesTheMap_AndDeletingTheListingCascades()
    {
        var client = await factory.GetOwnerClientAsync();
        var listingId = await CreateListingAsync(client);
        var missing = (await client.GraphQLAsync(Delete, new { listingId })).GetProperty("deleteRequirementMap");
        Assert.Equal("RequirementMapNotFoundError", missing.GetProperty("errors")[0].GetProperty("__typename").GetString());
        await SaveOkAsync(client, listingId, Need("Need", match: "GAP"));

        var deleted = (await client.GraphQLAsync(Delete, new { listingId })).GetProperty("deleteRequirementMap");

        Assert.Equal(listingId, deleted.GetProperty("listingId").GetInt32());
        Assert.Equal(JsonValueKind.Null, (await client.GraphQLAsync(Read, new { listingId })).GetProperty("requirementMap").ValueKind);

        await SaveOkAsync(client, listingId, Need("Need", match: "GAP"));
        await client.GraphQLAsync("mutation ($id: Int!) { deleteListing(input: { id: $id }) { errors { __typename } } }", new { id = listingId });
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.RequirementMaps.IgnoreQueryFilters().AnyAsync(m => m.ListingId == listingId));
    }

    [Fact]
    public async Task ExternalPrompt_HasRulesAndAllowlistedData()
    {
        var client = await factory.GetOwnerClientAsync();
        await client.GraphQLAsync("""
            mutation {
              updateProfile(input: { fullName: "PRIVATE_NAME", headline: "Engineer", summary: "PROFILE_SUMMARY",
                email: "PRIVATE_EMAIL@example.test", phone: "PRIVATE_PHONE", dateOfBirth: "1990-01-01",
                nationality: "PRIVATE_NATIONALITY", workAuthorization: "PRIVATE_AUTH", hobbies: [], links: [] }) { errors { __typename } }
              updateProfileAddress(input: { streetAddress: "PRIVATE_STREET", postalCode: "0150" }) { errors { __typename } }
              updateProfilePreferences(input: { jobRoleNames: [], workModes: [HYBRID], employmentTypes: [], minSalary: 987654, salaryCurrency: "NOK" }) { errors { __typename } }
            }
            """);
        var listingId = await CreateListingAsync(client);
        var (_, bulletId) = await AddEvidenceAsync(client, "PROMPT_BULLET_TEXT");

        var prompt = (await client.GraphQLAsync(Prompt, new { listingId })).GetProperty("requirementMapExternalPrompt");

        Assert.Equal(1, prompt.GetProperty("version").GetInt32());
        var instructions = prompt.GetProperty("instructions").GetString()!;
        foreach (var expected in new[]
        {
            "the profile shows nothing for it", "A similar tool is not the tool asked for", "untrusted data",
            "\"schemaVersion\": 1", "Build accessible interfaces", "https://requirement-map.example/", "PROMPT_BULLET_TEXT",
            $"\"id\":{bulletId}", "PROFILE_SUMMARY", "HYBRID",
        })
            Assert.Contains(expected, instructions);
        foreach (var secret in new[] { "PRIVATE_", "1990-01-01", "987654", "0150" })
            Assert.DoesNotContain(secret, instructions, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Preview_HoldsCandidatesToTheProfile()
    {
        var client = await factory.GetOwnerClientAsync();
        var listingId = await CreateListingAsync(client);
        var (experienceId, bulletId) = await AddEvidenceAsync(client);
        var json = MapJson(
            $$"""{"text":"Partly real","kind":"SKILL","importance":"REQUIRED","match":"EVIDENCED","bulletIds":[{{bulletId}},999999],"entries":[{"kind":"EXPERIENCE","id":{{experienceId}}},{"kind":"EDUCATION","id":999999}],"confidence":0.9}""",
            """{"text":"Invented","kind":"DUTY","importance":"PREFERRED","match":"EVIDENCED","bulletIds":[999999]}""",
            $$"""{"text":"{{new string('y', 250)}}","kind":"PRACTICAL","importance":"PREFERRED","match":"PARTIAL","note":"Oslo, hybrid"}""",
            """{"text":" ","kind":"DUTY","importance":"PREFERRED","match":"GAP"}""");

        var payload = (await client.GraphQLAsync(Preview, new { listingId, json })).GetProperty("previewRequirementMap");

        var needs = payload.GetProperty("candidate").GetProperty("needs");
        Assert.Equal(3, needs.GetArrayLength());
        Assert.Equal("EVIDENCED", needs[0].GetProperty("match").GetString());
        Assert.Equal([bulletId], needs[0].GetProperty("bulletIds").EnumerateArray().Select(i => i.GetInt32()));
        Assert.Equal(experienceId, Assert.Single(needs[0].GetProperty("entries").EnumerateArray()).GetProperty("id").GetInt32());
        Assert.Equal("GAP", needs[1].GetProperty("match").GetString());
        Assert.Empty(needs[1].GetProperty("bulletIds").EnumerateArray());
        Assert.Equal(200, needs[2].GetProperty("text").GetString()!.Length);
        Assert.Equal("PARTIAL", needs[2].GetProperty("match").GetString());
        Assert.Equal(JsonValueKind.Null, (await client.GraphQLAsync(Read, new { listingId })).GetProperty("requirementMap").ValueKind);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("   ")]
    [InlineData("""{"schemaVersion":1,"needs":[]}""")]
    [InlineData("""{"schemaVersion":1}""")]
    [InlineData("""{"needs":[{"text":"x","kind":"DUTY","importance":"REQUIRED","match":"GAP"}]}""")]
    [InlineData("""{"schemaVersion":1,"needs":[{"text":"x","kind":"NONSENSE","importance":"REQUIRED","match":"GAP"}]}""")]
    public async Task Preview_InvalidPaste_IsRejected(string json)
    {
        var client = await factory.GetOwnerClientAsync();
        var listingId = await CreateListingAsync(client);

        var payload = (await client.GraphQLAsync(Preview, new { listingId, json })).GetProperty("previewRequirementMap");

        Assert.Equal("InvalidRequirementMapError", payload.GetProperty("errors")[0].GetProperty("__typename").GetString());
    }

    [Fact]
    public async Task Generate_ReturnsACandidate_LogsUsage_AndSavesNothing()
    {
        var client = await factory.GetOwnerClientAsync();
        var listingId = await CreateListingAsync(client);
        var (_, bulletId) = await AddEvidenceAsync(client, "GENERATE_BULLET_TEXT");
        await SaveOkAsync(client, listingId, Need("Saved before", match: "GAP"));
        factory.Llm.Respond = () => new ChatResponse(new ChatMessage(ChatRole.Assistant,
            $$"""{"needs":[{"text":"React","kind":"SKILL","importance":"REQUIRED","match":"EVIDENCED","bulletIds":[{{bulletId}}]},{"text":"Kotlin","kind":"SKILL","importance":"PREFERRED","match":"EVIDENCED","bulletIds":[999999]}]}"""));

        var result = (await client.GraphQLAsync(Generate, new { listingId })).GetProperty("generateRequirementMap").GetProperty("result");

        Assert.Equal("SUCCEEDED", result.GetProperty("status").GetString());
        var needs = result.GetProperty("content").GetProperty("needs");
        Assert.Equal([bulletId], needs[0].GetProperty("bulletIds").EnumerateArray().Select(i => i.GetInt32()));
        Assert.Equal("GAP", needs[1].GetProperty("match").GetString());
        var prompt = string.Join("\n", factory.Llm.LastMessages.Select(m => m.Text));
        Assert.Contains("GENERATE_BULLET_TEXT", prompt);
        Assert.Contains("Build accessible interfaces", prompt);
        // Only an external chat can browse, so the built-in path sends no url.
        Assert.DoesNotContain("https://requirement-map.example/", prompt, StringComparison.Ordinal);
        using var scope = factory.Services.CreateScope();
        var usage = await scope.ServiceProvider.GetRequiredService<AppDbContext>().LlmUsages
            .IgnoreQueryFilters().OrderByDescending(u => u.Id).FirstAsync();
        Assert.Equal("requirement-map", usage.Operation);
        Assert.True(usage.Success);
        var saved = (await client.GraphQLAsync(Read, new { listingId })).GetProperty("requirementMap");
        Assert.Equal("Saved before", saved.GetProperty("content").GetProperty("needs")[0].GetProperty("text").GetString());
    }

    [Fact]
    public async Task Generate_UnreadableAnswer_Fails()
    {
        var client = await factory.GetOwnerClientAsync();
        var listingId = await CreateListingAsync(client);
        factory.Llm.Respond = () => new ChatResponse(new ChatMessage(ChatRole.Assistant, """{"map":"nope"}"""));

        var result = (await client.GraphQLAsync(Generate, new { listingId })).GetProperty("generateRequirementMap").GetProperty("result");

        Assert.Equal("FAILED", result.GetProperty("status").GetString());
        Assert.Equal("invalid-json", result.GetProperty("errorCode").GetString());
        Assert.NotNull(result.GetProperty("error").GetString());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("content").ValueKind);
    }

    [Fact]
    public async Task Generate_UnknownModelOrEmptyListing_FailsBeforeAnyProviderCall()
    {
        var client = await factory.GetOwnerClientAsync();
        var listingId = await CreateListingAsync(client);
        var emptyListingId = await CreateListingAsync(client, description: null);
        var calls = factory.Llm.Calls;

        var unknown = (await client.GraphQLAsync(Generate, new { listingId, model = "no-such-model" })).GetProperty("generateRequirementMap");
        var empty = (await client.GraphQLAsync(Generate, new { listingId = emptyListingId })).GetProperty("generateRequirementMap");

        Assert.Equal("UnknownLlmModelError", unknown.GetProperty("errors")[0].GetProperty("__typename").GetString());
        Assert.Equal("InvalidRequirementMapError", empty.GetProperty("errors")[0].GetProperty("__typename").GetString());
        Assert.Equal(calls, factory.Llm.Calls);
        Assert.Contains("no description or skills", await client.GraphQLErrorAsync(Prompt, new { listingId = emptyListingId }));
    }

    private static object Need(string text, string kind = "SKILL", string importance = "REQUIRED", string match = "EVIDENCED",
        int[]? bulletIds = null, object[]? entries = null, string? note = null) =>
        new { text, kind, importance, match, bulletIds, entries, note };

    private static string MapJson(params string[] needs) => $$"""{"schemaVersion":1,"needs":[{{string.Join(",", needs)}}]}""";

    private static async Task<JsonElement> SaveOkAsync(HttpClient client, int listingId, params object[] needs) =>
        (await client.GraphQLAsync(Save, new { listingId, needs })).GetProperty("saveRequirementMap").GetProperty("requirementMap");

    private static async Task<(int ExperienceId, int BulletId)> AddEvidenceAsync(HttpClient client, string text = "Built a React design system")
    {
        var experienceId = (await ProfileEntryTests.AddExperienceAsync(client, $"Employer {Guid.NewGuid():N}")).GetProperty("id").GetInt32();
        var bullet = await client.GraphQLAsync("""
            mutation ($entryId: Int!, $text: String!) {
              addProfileBullet(input: { kind: EXPERIENCE, entryId: $entryId, text: $text }) { profileBullet { id } }
            }
            """, new { entryId = experienceId, text });
        return (experienceId, bullet.GetProperty("addProfileBullet").GetProperty("profileBullet").GetProperty("id").GetInt32());
    }

    private static async Task<int> CreateListingAsync(HttpClient client, string? description = "Build accessible interfaces")
    {
        var listing = new
        {
            title = "Frontend developer", companyName = "Requirement map company", jobRoleName = "Developer",
            description, url = $"https://requirement-map.example/{Guid.NewGuid()}", postedOn = "2026-10-01",
            deadlineType = "UNSPECIFIED", skills = Array.Empty<object>(), languages = Array.Empty<object>(),
            locations = Array.Empty<object>(),
        };
        var data = await client.GraphQLAsync("""
            mutation ($listing: ListingInput!) { createListing(input: { listing: $listing }) { listing { id } } }
            """, new { listing });
        return data.GetProperty("createListing").GetProperty("listing").GetProperty("id").GetInt32();
    }
}
