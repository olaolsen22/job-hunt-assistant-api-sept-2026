using System.Text.Json;
using JobHunt.Api.Features.TailoredCvDrafts;
using JobHunt.Api.Tests.Features.Profiles;
using Microsoft.Extensions.AI;

namespace JobHunt.Api.Tests.Features.TailoredCvDrafts;

public class TailoredCvTemplateTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Analyze = """
        mutation ($listingId: Int!, $section: TailoredCvSection!, $sourceEntryId: Int, $template: TailoredCvTemplate) {
          analyzeTailoredCvSection(input: { listingId: $listingId, section: $section, sourceEntryId: $sourceEntryId, template: $template }) {
            result { status selectedBulletIds paragraph bullets { sourceBulletId text } error }
            errors { __typename ... on Error { message } }
          }
        }
        """;
    private const string Preview = """
        mutation ($listingId: Int!, $json: String!, $template: TailoredCvTemplate) {
          previewTailoredCvDraft(input: { listingId: $listingId, json: $json, template: $template }) {
            candidate { content { template experiences { bullets { sourceBulletId text } } } }
            errors { __typename ... on Error { message } }
          }
        }
        """;

    [Fact]
    public async Task DetailedAnalysis_ReturnsSourceBulletTextAndKeepsParagraph()
    {
        var client = await factory.GetOwnerClientAsync();
        var (entryId, bulletIds) = await AddExperienceAsync(client, "Detailed Employer", "2020-01-01", null,
            "Built a React application for internal users");
        var listingId = await TailoredCvAnalysisTests.CreateListingAsync(client, "React developer", "Build React apps", []);
        factory.Llm.Respond = () => Reply($$"""{"selectedBulletIds":[{{bulletIds[0]}}],"paragraph":"Built a React application."}""");

        var result = await AnalyzeAsync(client, listingId, "EXPERIENCE", entryId, "DETAILED");

        Assert.Equal("SUCCEEDED", result.GetProperty("status").GetString());
        Assert.Equal("Built a React application.", result.GetProperty("paragraph").GetString());
        var bullet = Assert.Single(result.GetProperty("bullets").EnumerateArray());
        Assert.Equal(bulletIds[0], bullet.GetProperty("sourceBulletId").GetInt32());
        Assert.Equal("Built a React application for internal users", bullet.GetProperty("text").GetString());
        var instructions = factory.Llm.LastMessages[0].Text;
        Assert.DoesNotContain("Shorten each selected bullet", instructions);
        Assert.Contains("1-2 pages", instructions);
        Assert.Contains("no em dashes", instructions);
    }

    [Fact]
    public async Task ConciseAnalysis_NewestRole_ShortensBulletsAndFallsBackWhenWordsAreAdded()
    {
        var client = await factory.GetOwnerClientAsync();
        var (entryId, bulletIds) = await AddExperienceAsync(client, "Concise Employer", "2030-01-01", null,
            "Designed and built several internal web services for the payments team using C# and ASP.NET Core",
            "Worked on deployment scripts with the team");
        var listingId = await TailoredCvAnalysisTests.CreateListingAsync(client, "Backend developer",
            "You will build REST APIs and lead a team of 5 engineers", ["ASP.NET Core"]);
        factory.Llm.Respond = () => Reply($$"""
            {"bullets":[
              {"id":{{bulletIds[0]}},"text":"Built REST APIs for the payments team using C# and ASP.NET Core"},
              {"id":{{bulletIds[1]}},"text":"Led a team of 5 engineers automating deployments"}
            ],"technologySkillIds":[]}
            """);

        var result = await AnalyzeAsync(client, listingId, "EXPERIENCE", entryId, "CONCISE");

        Assert.Equal("SUCCEEDED", result.GetProperty("status").GetString());
        Assert.Equal(bulletIds, result.GetProperty("selectedBulletIds").EnumerateArray().Select(v => v.GetInt32()));
        Assert.Equal(JsonValueKind.Null, result.GetProperty("paragraph").ValueKind);
        var texts = result.GetProperty("bullets").EnumerateArray().Select(b => b.GetProperty("text").GetString()).ToList();
        // The listing's "REST APIs" may replace the bullet's wording; an invented lead role and headcount may not.
        Assert.Equal("Built REST APIs for the payments team using C# and ASP.NET Core", texts[0]);
        Assert.Equal("Worked on deployment scripts with the team", texts[1]);
        var instructions = factory.Llm.LastMessages[0].Text;
        Assert.Contains("Select at most 5 of the existing experience bullet IDs", instructions);
        Assert.Contains("Shorten each selected bullet", instructions);
        Assert.Contains("one page", instructions);
    }

    [Fact]
    public async Task ConciseAnalysis_OlderRole_AllowsTwoBullets()
    {
        var client = await factory.GetOwnerClientAsync();
        for (var i = 0; i < 3; i++)
            await ProfileEntryTests.AddExperienceAsync(client, employer: $"Newer {i}", start: "2029-01-01", skills: []);
        var (entryId, _) = await AddExperienceAsync(client, "Old Employer", "1990-01-01", "1991-01-01", "Maintained reports");
        var listingId = await TailoredCvAnalysisTests.CreateListingAsync(client, "Developer", "Build software", []);
        factory.Llm.Respond = () => Reply("""{"bullets":[]}""");

        var result = await AnalyzeAsync(client, listingId, "EXPERIENCE", entryId, "CONCISE");

        Assert.Equal("SUCCEEDED", result.GetProperty("status").GetString());
        Assert.Contains("Select at most 2 of the existing experience bullet IDs", factory.Llm.LastMessages[0].Text);
    }

    [Fact]
    public async Task ConciseAnalysis_UnknownBulletId_FailsTheSection()
    {
        var client = await factory.GetOwnerClientAsync();
        var (entryId, _) = await AddExperienceAsync(client, "Unknown bullet", "2020-01-01", null, "Built services");
        var listingId = await TailoredCvAnalysisTests.CreateListingAsync(client, "Developer", "Build software", []);
        factory.Llm.Respond = () => Reply("""{"bullets":[{"id":2147483647,"text":"Built services"}]}""");

        var result = await AnalyzeAsync(client, listingId, "EXPERIENCE", entryId, "CONCISE");

        Assert.Equal("FAILED", result.GetProperty("status").GetString());
    }

    [Fact]
    public async Task ConciseSummaryAndSkills_AskForShorterOutput()
    {
        var client = await factory.GetOwnerClientAsync();
        var listingId = await TailoredCvAnalysisTests.CreateListingAsync(client, "Developer", "Build software", []);

        factory.Llm.Respond = () => Reply("""{"summary":null}""");
        await AnalyzeAsync(client, listingId, "SUMMARY", null, "CONCISE");
        Assert.Contains("at most 220 characters", factory.Llm.LastMessages[0].Text);

        factory.Llm.Respond = () => Reply("""{"selectedSkillIds":[]}""");
        await AnalyzeAsync(client, listingId, "SKILLS", null, "CONCISE");
        Assert.Contains("8-12 skills", factory.Llm.LastMessages[0].Text);
    }

    [Fact]
    public async Task AnalyzeAll_SummarySeesTheSelectedBullets()
    {
        var client = await factory.GetOwnerClientAsync();
        var (_, bulletIds) = await AddExperienceAsync(client, "Consistency Employer", "2031-01-01", null,
            "Built a payments API in ASP.NET Core", "Organised the team lunch");
        var listingId = await TailoredCvAnalysisTests.CreateListingAsync(client, "Backend developer", "Build APIs", []);
        factory.Llm.Respond = () =>
        {
            var instructions = factory.Llm.LastMessages[0].Text;
            var json = instructions.Contains(" bullet IDs ", StringComparison.Ordinal)
                ? factory.Llm.LastMessages[1].Text.Contains($"\"id\":{bulletIds[0]},", StringComparison.Ordinal)
                    ? $$"""{"selectedBulletIds":[{{bulletIds[0]}}],"paragraph":"Built a payments API."}"""
                    : "{\"selectedBulletIds\":[]}"
                : instructions.Contains("profile skill IDs", StringComparison.Ordinal)
                    ? "{\"selectedSkillIds\":[]}" : "{\"summary\":null}";
            return Reply(json);
        };

        await client.GraphQLAsync("""
            mutation ($listingId: Int!) {
              analyzeAllTailoredCv(input: { listingId: $listingId }) { analysis { results { status } } }
            }
            """, new { listingId });

        Assert.Contains("The summary and the CV body must agree", factory.Llm.LastMessages[0].Text);
        var context = factory.Llm.LastMessages[1].Text;
        var cvBullets = context[context.IndexOf("\"cvBullets\"", StringComparison.Ordinal)..context.IndexOf("\"profile\"", StringComparison.Ordinal)];
        Assert.Contains("Built a payments API in ASP.NET Core", cvBullets);
        Assert.DoesNotContain("Organised the team lunch", cvBullets);
    }

    [Fact]
    public async Task ExternalPrompt_ConciseAsksForShortenedBulletsAndCaps()
    {
        var client = await factory.GetOwnerClientAsync();
        var listingId = await TailoredCvAnalysisTests.CreateListingAsync(client, "Developer", "Build software", []);
        const string Query = """
            query ($listingId: Int!, $template: TailoredCvTemplate) {
              tailoredCvExternalPrompt(listingId: $listingId, template: $template) { version instructions }
            }
            """;

        var concise = (await client.GraphQLAsync(Query, new { listingId, template = "CONCISE" }))
            .GetProperty("tailoredCvExternalPrompt");
        var detailed = (await client.GraphQLAsync(Query, new { listingId, template = "DETAILED" }))
            .GetProperty("tailoredCvExternalPrompt").GetProperty("instructions").GetString()!;

        Assert.Equal(TailoredCvExternalPromptQueries.Version, concise.GetProperty("version").GetInt32());
        var instructions = concise.GetProperty("instructions").GetString()!;
        Assert.Contains("Shorten each selected bullet", instructions);
        Assert.Contains("at most 5 bullets for the newest experience", instructions);
        Assert.Contains("every requirement the profile supports is shown", instructions);
        Assert.Contains("The summary and the CV body must agree", detailed);
        Assert.Contains("\"template\": \"CONCISE\"", instructions);
        Assert.Contains("at most 220 characters", instructions);
        Assert.DoesNotContain("preserve the exact original bullet text", instructions);
        Assert.Contains("preserve the exact original bullet text", detailed);
        Assert.Contains("\"template\": \"DETAILED\"", detailed);
        Assert.Contains("no em dashes", detailed);
    }

    [Fact]
    public async Task Preview_Concise_AcceptsShortenedTextAndFallsBackOnAddedWords()
    {
        var client = await factory.GetOwnerClientAsync();
        var (entryId, bulletIds) = await AddExperienceAsync(client, "Preview Employer", "2020-01-01", null,
            "Designed and built internal web services for the payments team", "Wrote deployment scripts");
        var listingId = await TailoredCvAnalysisTests.CreateListingAsync(client, "Backend developer", "Build REST APIs", []);
        var json = Candidate(entryId, (bulletIds[0], "Built REST APIs for the payments team"),
            (bulletIds[1], "Architected a cloud platform serving 2 million users"));

        var payload = (await client.GraphQLAsync(Preview, new { listingId, json, template = "CONCISE" }))
            .GetProperty("previewTailoredCvDraft");

        Assert.Equal(JsonValueKind.Null, payload.GetProperty("errors").ValueKind);
        var content = payload.GetProperty("candidate").GetProperty("content");
        Assert.Equal("CONCISE", content.GetProperty("template").GetString());
        var texts = content.GetProperty("experiences")[0].GetProperty("bullets").EnumerateArray()
            .Select(b => b.GetProperty("text").GetString()).ToList();
        Assert.Equal(["Built REST APIs for the payments team", "Wrote deployment scripts"], texts);
    }

    [Fact]
    public async Task Preview_Detailed_StillRejectsChangedBulletText()
    {
        var client = await factory.GetOwnerClientAsync();
        var (entryId, bulletIds) = await AddExperienceAsync(client, "Exact Employer", "2020-01-01", null,
            "Designed and built internal web services");
        var listingId = await TailoredCvAnalysisTests.CreateListingAsync(client, "Developer", "Build web services", []);
        var json = Candidate(entryId, (bulletIds[0], "Built web services"));

        var payload = (await client.GraphQLAsync(Preview, new { listingId, json, template = "DETAILED" }))
            .GetProperty("previewTailoredCvDraft");

        Assert.Equal("InvalidTailoredCvDraftError", payload.GetProperty("errors")[0].GetProperty("__typename").GetString());
    }

    [Fact]
    public async Task Preview_Concise_RejectsAnotherUsersBullet()
    {
        var owner = await factory.GetOwnerClientAsync();
        var (entryId, bulletIds) = await AddExperienceAsync(owner, "Owner Employer", "2020-01-01", null, "Built services");
        var guest = await factory.GetGuestClientAsync();
        var listingId = await TailoredCvAnalysisTests.CreateListingAsync(guest, "Developer", "Build services", []);

        var payload = (await guest.GraphQLAsync(Preview,
                new { listingId, json = Candidate(entryId, (bulletIds[0], "Built services")), template = "CONCISE" }))
            .GetProperty("previewTailoredCvDraft");

        Assert.Equal("InvalidTailoredCvDraftError", payload.GetProperty("errors")[0].GetProperty("__typename").GetString());
    }

    [Fact]
    public async Task Save_RoundTripsTemplate_AndOldDraftsReadAsDetailed()
    {
        var client = await factory.GetOwnerClientAsync();
        var listingId = await TailoredCvAnalysisTests.CreateListingAsync(client, "Developer", "Build software", []);

        var saved = await client.GraphQLAsync("""
            mutation ($listingId: Int!) {
              saveTailoredCvDraft(input: { listingId: $listingId, content: { template: CONCISE } }) {
                tailoredCvDraft { content { template } }
              }
            }
            """, new { listingId });

        Assert.Equal("CONCISE", saved.GetProperty("saveTailoredCvDraft").GetProperty("tailoredCvDraft")
            .GetProperty("content").GetProperty("template").GetString());
        Assert.Equal(TailoredCvTemplate.Detailed, new TailoredCvDraft { ContentJson = """{"summary":"Old"}""" }.Content.Template);
    }

    [Theory]
    [InlineData("Built REST APIs in C# and .NET", true)]
    [InlineData("Utviklet tjenester i Node.js", true)]
    [InlineData("Led a large team building services", false)]
    [InlineData("Built services for 5 teams", false)]
    public void KeepsSourceWords_AllowsCutsListingTermsAndSmallGrammarChanges(string text, bool expected) =>
        Assert.Equal(expected, TailoredCvWording.KeepsSourceWords(text,
            ["Built several backend services in C# and .NET", "Utvikle tjenester i Node.js"],
            TailoredCvWording.ListingText("Backend developer", "Build REST APIs with 5 teams", ["C#"])));

    private async Task<JsonElement> AnalyzeAsync(HttpClient client, int listingId, string section, int? sourceEntryId,
        string template) =>
        (await client.GraphQLAsync(Analyze, new { listingId, section, sourceEntryId, template }))
            .GetProperty("analyzeTailoredCvSection").GetProperty("result");

    private static async Task<(int EntryId, List<int> BulletIds)> AddExperienceAsync(HttpClient client, string employer,
        string start, string? end, params string[] bullets)
    {
        var entryId = (await ProfileEntryTests.AddExperienceAsync(client, employer: employer, start: start, end: end, skills: []))
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

    private static string Candidate(int entryId, params (int Id, string Text)[] bullets) => JsonSerializer.Serialize(new
    {
        schemaVersion = 1,
        content = new
        {
            experiences = new[] { new
            {
                sourceEntryId = entryId, employer = "Employer", title = "Developer",
                bullets = bullets.Select(b => new { sourceBulletId = b.Id, text = b.Text }).ToArray(),
            } },
        },
    });

    private static ChatResponse Reply(string json) => new(new ChatMessage(ChatRole.Assistant, json));
}
