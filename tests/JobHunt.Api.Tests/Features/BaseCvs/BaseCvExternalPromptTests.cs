using System.Net.Http.Json;
using System.Text.Json;
using JobHunt.Api.Features.BaseCvs;
using JobHunt.Api.Tests.Features.Listings;
using JobHunt.Api.Tests.Features.Profiles;

namespace JobHunt.Api.Tests.Features.BaseCvs;

public class BaseCvExternalPromptTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Prompt = """
        query ($language: Language!, $targetRole: String!, $notes: String, $template: TailoredCvTemplate) {
          baseCvExternalPrompt(language: $language, targetRole: $targetRole, notes: $notes, template: $template) { version instructions }
        }
        """;
    private const string Preview = """
        mutation ($language: Language!, $json: String!, $template: TailoredCvTemplate) {
          previewBaseCv(input: { language: $language, json: $json, template: $template }) {
            candidate { content { language template fullName email headline experiences { bullets { text sourceBulletId } } } }
            errors { __typename ... on Error { message } }
          }
        }
        """;

    [Fact]
    public async Task Prompt_HasTheProfileTargetAndNotes_WithoutContactDetails()
    {
        var client = await factory.GetOwnerClientAsync();
        var (_, bulletIds) = await AddExperienceAsync(client, "Prompt Base Employer", "Built the checkout");

        var prompt = (await client.GraphQLAsync(Prompt, new
        {
            language = "ENGLISH", targetRole = " Frontend developer ", notes = "Aim at fintech product teams", template = (string?)null,
        })).GetProperty("baseCvExternalPrompt");

        Assert.Equal(BaseCvExternalPromptQueries.Version, prompt.GetProperty("version").GetInt32());
        var instructions = prompt.GetProperty("instructions").GetString()!;
        Assert.Contains("\"role\":\"Frontend developer\"", instructions);
        Assert.Contains("Aim at fintech product teams", instructions);
        Assert.Contains("Prompt Base Employer", instructions);
        Assert.Contains($"\"id\":{bulletIds[0]}", instructions);
        Assert.Contains("cherry-pick", instructions);
        Assert.Contains("combine it with", instructions);
        Assert.Contains("copy the bullet exactly as supplied", instructions);
        Assert.Contains("\"template\": \"DETAILED\"", instructions);
        Assert.DoesNotContain("companyResearch", instructions);
        Assert.DoesNotContain("\"email\"", instructions);
    }

    [Fact]
    public async Task Prompt_Concise_ShortensAndCaps()
    {
        var client = await factory.GetOwnerClientAsync();

        var instructions = (await client.GraphQLAsync(Prompt, new
        {
            language = "NORWEGIAN", targetRole = "Frontend", notes = (string?)null, template = "CONCISE",
        })).GetProperty("baseCvExternalPrompt").GetProperty("instructions").GetString()!;

        Assert.Contains("shorten it by cutting words", instructions);
        Assert.Contains("at most 5 bullets for the newest experience", instructions);
        Assert.Contains("\"template\": \"CONCISE\"", instructions);
        Assert.Contains("\"language\" to \"NORWEGIAN\"", instructions);
    }

    [Theory]
    [InlineData("   ", null, "targetRole")]
    [InlineData("Frontend", 2001, "notes")]
    public async Task Prompt_RejectsInvalidTarget(string targetRole, int? notesLength, string expected)
    {
        var client = await factory.GetOwnerClientAsync();
        var notes = notesLength is { } length ? new string('x', length) : null;

        var response = await client.PostAsJsonAsync("/graphql", new
        {
            query = Prompt,
            variables = new { language = "ENGLISH", targetRole, notes, template = (string?)null },
        });

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(expected, body.GetProperty("errors")[0].GetProperty("message").GetString());
    }

    [Fact]
    public async Task Preview_KeepsCopiedAndCombinedBullets_AndAddsProfileContact()
    {
        var client = await factory.GetOwnerClientAsync();
        await client.GraphQLAsync("""
            mutation { updateProfile(input: { fullName: "Base Owner", email: "base@example.com", hobbies: [], links: [] }) { profile { id } } }
            """);
        var (entryId, bulletIds) = await AddExperienceAsync(client, "Combine Employer",
            "Built the checkout in Vue", "Wrote deployment scripts", "Maintained the build pipeline");
        var combined = "Wrote deployment scripts and maintained the build pipeline";
        var json = Candidate(entryId, (bulletIds[0], "Built the checkout in Vue"), (bulletIds[1], combined));

        var payload = (await client.GraphQLAsync(Preview, new { language = "ENGLISH", json, template = (string?)null }))
            .GetProperty("previewBaseCv");

        Assert.Equal(JsonValueKind.Null, payload.GetProperty("errors").ValueKind);
        var content = payload.GetProperty("candidate").GetProperty("content");
        Assert.Equal("ENGLISH", content.GetProperty("language").GetString());
        Assert.Equal("DETAILED", content.GetProperty("template").GetString());
        var bullets = content.GetProperty("experiences")[0].GetProperty("bullets");
        Assert.Equal("Built the checkout in Vue", bullets[0].GetProperty("text").GetString());
        Assert.Equal(combined, bullets[1].GetProperty("text").GetString());
        Assert.Equal(bulletIds[1], bullets[1].GetProperty("sourceBulletId").GetInt32());
        Assert.Equal("Base Owner", content.GetProperty("fullName").GetString());
        Assert.Equal("base@example.com", content.GetProperty("email").GetString());
    }

    [Fact]
    public async Task Preview_FallsBackToTheSourceText_OnInventedWordsOrNumbers()
    {
        var client = await factory.GetOwnerClientAsync();
        var (entryId, bulletIds) = await AddExperienceAsync(client, "Fallback Employer",
            "Wrote deployment scripts", "Maintained the build pipeline");
        var json = Candidate(entryId, (bulletIds[0], "Architected a cloud platform for global customers"),
            (bulletIds[1], "Maintained the build pipeline for 40 teams"));

        var bullets = (await client.GraphQLAsync(Preview, new { language = "ENGLISH", json, template = "CONCISE" }))
            .GetProperty("previewBaseCv").GetProperty("candidate").GetProperty("content")
            .GetProperty("experiences")[0].GetProperty("bullets");

        Assert.Equal("Wrote deployment scripts", bullets[0].GetProperty("text").GetString());
        Assert.Equal("Maintained the build pipeline", bullets[1].GetProperty("text").GetString());
    }

    [Fact]
    public async Task Preview_RejectsABulletWithoutASource()
    {
        var client = await factory.GetOwnerClientAsync();
        var (entryId, _) = await AddExperienceAsync(client, "Sourceless Employer", "Built the checkout");
        var json = Candidate(entryId, (null, "Led a team of ten"));

        var payload = (await client.GraphQLAsync(Preview, new { language = "ENGLISH", json, template = (string?)null }))
            .GetProperty("previewBaseCv");

        Assert.Equal("InvalidTailoredCvDraftError", payload.GetProperty("errors")[0].GetProperty("__typename").GetString());
        Assert.Contains("source bullet", payload.GetProperty("errors")[0].GetProperty("message").GetString());
    }

    [Fact]
    public async Task Preview_RejectsABulletFromAnotherEntryOrUser()
    {
        var owner = await factory.GetOwnerClientAsync();
        var (entryId, _) = await AddExperienceAsync(owner, "Own Employer", "Built the checkout");
        var (_, otherEntryBullets) = await AddExperienceAsync(owner, "Other Employer", "Ran the design system");
        var guest = await factory.GetGuestClientAsync();
        var (_, guestBullets) = await AddExperienceAsync(guest, "Guest Employer", "Guest work");

        foreach (var (id, text) in new[] { (otherEntryBullets[0], "Ran the design system"), (guestBullets[0], "Guest work") })
        {
            var json = Candidate(entryId, (id, text));
            var payload = (await owner.GraphQLAsync(Preview, new { language = "ENGLISH", json, template = (string?)null }))
                .GetProperty("previewBaseCv");
            Assert.Equal("InvalidTailoredCvDraftError", payload.GetProperty("errors")[0].GetProperty("__typename").GetString());
            Assert.Contains("sourceBulletId", payload.GetProperty("errors")[0].GetProperty("message").GetString());
        }
    }

    [Fact]
    public async Task Preview_RejectsInvalidJson()
    {
        var client = await factory.GetOwnerClientAsync();

        var payload = (await client.GraphQLAsync(Preview, new { language = "ENGLISH", json = "not json", template = (string?)null }))
            .GetProperty("previewBaseCv");

        Assert.Equal("InvalidTailoredCvDraftError", payload.GetProperty("errors")[0].GetProperty("__typename").GetString());
    }

    private static async Task<(int EntryId, List<int> BulletIds)> AddExperienceAsync(HttpClient client, string employer, params string[] bullets)
    {
        var entryId = (await ProfileEntryTests.AddExperienceAsync(client, employer: ListingTests.Unique(employer), skills: []))
            .GetProperty("id").GetInt32();
        var ids = new List<int>();
        foreach (var text in bullets)
            ids.Add((await client.GraphQLAsync("""
                mutation ($entryId: Int!, $text: String!) {
                  addProfileBullet(input: { kind: EXPERIENCE, entryId: $entryId, text: $text }) { profileBullet { id } }
                }
                """, new { entryId, text })).GetProperty("addProfileBullet").GetProperty("profileBullet").GetProperty("id").GetInt32());
        return (entryId, ids);
    }

    private static string Candidate(int entryId, params (int? Id, string Text)[] bullets) => JsonSerializer.Serialize(new
    {
        schemaVersion = 1,
        content = new
        {
            experiences = new[] { new
            {
                sourceEntryId = entryId, employer = "Employer", title = "Developer", startMonth = "2020-01-01", endMonth = (string?)null,
                bullets = bullets.Select(b => new { sourceBulletId = b.Id, text = b.Text }),
            } },
        },
    });
}
