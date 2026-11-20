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
            candidate { content { template experiences { paragraph bullets { sourceBulletId text } } } }
            errors { __typename ... on Error { message } }
          }
        }
        """;

    [Theory]
    [InlineData("Streamlined development process from start to finish within cross-functional product, design, and mobile teams, making it ~30% faster with a more consistent UI.", true)]
    [InlineData("Streamlined development process within cross-functional product, design, and mobile teams, making it ~40% faster.", false)]
    [InlineData("Led every cross-functional team at the company and owned the whole web platform alone.", false)]
    public async Task DetailedAnalysis_KeepsListingWordingAndFallsBackOtherwise(string text, bool kept)
    {
        var client = await factory.GetOwnerClientAsync();
        var (entryId, bulletIds) = await AddExperienceAsync(client, "Detailed Employer", "2020-01-01", null, TeamsBullet);
        var listingId = await TailoredCvAnalysisTests.CreateListingAsync(client, "Frontend developer", TeamsListing, []);
        factory.Llm.Respond = () => Reply(JsonSerializer.Serialize(new
        {
            bullets = new[] { new { id = bulletIds[0], text } },
            paragraph = "Worked within cross-functional teams to speed up delivery.",
        }));

        var result = await AnalyzeAsync(client, listingId, "EXPERIENCE", entryId, "DETAILED");

        Assert.Equal("SUCCEEDED", result.GetProperty("status").GetString());
        // A DETAILED paragraph may rephrase freely.
        Assert.Equal("Worked within cross-functional teams to speed up delivery.", result.GetProperty("paragraph").GetString());
        var bullet = Assert.Single(result.GetProperty("bullets").EnumerateArray());
        Assert.Equal(bulletIds[0], bullet.GetProperty("sourceBulletId").GetInt32());
        Assert.Equal(kept ? text : TeamsBullet, bullet.GetProperty("text").GetString());
        var instructions = factory.Llm.LastMessages[0].Text;
        Assert.DoesNotContain("Shorten each selected bullet", instructions);
        Assert.Contains("reword it with the listing's term", instructions);
        Assert.Contains("1-2 pages", instructions);
        Assert.Contains("no em dashes", instructions);
    }

    [Fact]
    public async Task ConciseAnalysis_NewestRole_MergesBulletsAndFallsBackOnInventedFacts()
    {
        var client = await factory.GetOwnerClientAsync();
        var (entryId, bulletIds) = await AddExperienceAsync(client, "Concise Employer", "2040-01-01", null,
            "Designed and built several internal web services for the payments team using C# and ASP.NET Core",
            "Worked on deployment scripts with the team", "Ran the weekly demo");
        var listingId = await TailoredCvAnalysisTests.CreateListingAsync(client, "Backend developer",
            "You will build REST APIs, use Kubernetes and lead a team of 5 engineers", ["ASP.NET Core", "Kubernetes"]);
        factory.Llm.Respond = () => Reply($$"""
            {"bullets":[
              {"id":{{bulletIds[0]}},"text":"Built payments services and their deployment scripts in C# and ASP.NET Core"},
              {"id":{{bulletIds[1]}},"text":"Led a team of 5 engineers automating deployments"},
              {"id":{{bulletIds[2]}},"text":"Presented the team's work in a weekly demo on Kubernetes"}
            ],"technologySkillIds":[]}
            """);

        var result = await AnalyzeAsync(client, listingId, "EXPERIENCE", entryId, "CONCISE");

        Assert.Equal("SUCCEEDED", result.GetProperty("status").GetString());
        Assert.Equal(bulletIds, result.GetProperty("selectedBulletIds").EnumerateArray().Select(v => v.GetInt32()));
        Assert.Equal(JsonValueKind.Null, result.GetProperty("paragraph").ValueKind);
        var texts = result.GetProperty("bullets").EnumerateArray().Select(b => b.GetProperty("text").GetString()).ToList();
        // A merged, reworded bullet keeps its entry's facts; an invented headcount or listing skill falls back.
        Assert.Equal("Built payments services and their deployment scripts in C# and ASP.NET Core", texts[0]);
        Assert.Equal("Worked on deployment scripts with the team", texts[1]);
        Assert.Equal("Ran the weekly demo", texts[2]);
        var instructions = factory.Llm.LastMessages[0].Text;
        Assert.Contains("Write at most 5 CV bullets for this experience", instructions);
        Assert.Contains("raw notes, not a finished CV", instructions);
        Assert.Contains("Merge related bullets of the same entry", instructions);
        Assert.Contains("one page", instructions);
    }

    [Theory]
    [InlineData("Trained 3 junior developers and worked with the design team.", true)]
    [InlineData("Trained 3 junior developers and served 200 customers a day with a smile.", false)]
    // An overview of the role may use an unselected bullet's words.
    [InlineData("Kept the shop clean and trained 3 junior developers.", true)]
    [InlineData("Developed software in a team and mentored new colleagues.", true)]
    public async Task ConciseAnalysis_KeepsParagraphToTheEntrysFacts(string paragraph, bool kept)
    {
        var client = await factory.GetOwnerClientAsync();
        var (entryId, bulletIds) = await AddExperienceAsync(client, "Paragraph Employer", "2020-01-01", null,
            "Mentored and trained 3 junior developers", "Worked with the design team on UI changes", "Kept the shop clean");
        var listingId = await TailoredCvAnalysisTests.CreateListingAsync(client, "Barista", "Serve coffee", []);
        factory.Llm.Respond = () => Reply(JsonSerializer.Serialize(new
        {
            bullets = bulletIds.Take(2).Select(id => new { id, text = "Worked with the design team" }),
            paragraph,
        }));

        var result = await AnalyzeAsync(client, listingId, "EXPERIENCE", entryId, "CONCISE");

        Assert.Equal("SUCCEEDED", result.GetProperty("status").GetString());
        Assert.Equal(kept ? paragraph : null, result.GetProperty("paragraph").GetString());
        Assert.Contains("short overview paragraph of at most 2 sentences", factory.Llm.LastMessages[0].Text);
    }

    [Theory]
    [InlineData("EXPERIENCE", "outside the field of most of the candidate's experience")]
    [InlineData("EXPERIENCE", "the entry then shows only its title and dates")]
    [InlineData("EXPERIENCE", "IT jargon such as UI, UX, frontend, backend")]
    [InlineData("EXPERIENCE", "this overrides any rule here about keeping such facts")]
    [InlineData("SKILLS", "leave out the technical stack")]
    [InlineData("SUMMARY", "present them by that title instead")]
    public async Task Analysis_AimsAtAListingOutsideTheCandidatesField(string section, string expected)
    {
        var client = await factory.GetOwnerClientAsync();
        var (entryId, _) = await AddExperienceAsync(client, "Field Employer", "2020-01-01", null, "Built services");
        var listingId = await TailoredCvAnalysisTests.CreateListingAsync(client, "Barista", "Serve coffee", []);
        factory.Llm.Respond = () => Reply(section switch
        {
            "SKILLS" => """{"selectedSkillIds":[]}""",
            "SUMMARY" => """{"summary":null}""",
            _ => """{"bullets":[]}""",
        });

        foreach (var template in new[] { "DETAILED", "CONCISE" })
        {
            await AnalyzeAsync(client, listingId, section, section == "EXPERIENCE" ? entryId : null, template);
            Assert.Contains(expected, factory.Llm.LastMessages[0].Text.ReplaceLineEndings(" "));
        }
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
        Assert.Contains("Write at most 2 CV bullets for this experience", factory.Llm.LastMessages[0].Text);
    }

    [Fact]
    public async Task ConciseAnalysis_LongOlderRole_AllowsFiveBullets()
    {
        var client = await factory.GetOwnerClientAsync();
        for (var i = 0; i < 3; i++)
            await ProfileEntryTests.AddExperienceAsync(client, employer: $"Internship {i}", start: "2029-01-01", skills: []);
        var (entryId, _) = await AddExperienceAsync(client, "Senior Employer", "1990-01-01", "1994-01-01", "Led the frontend team");
        var listingId = await TailoredCvAnalysisTests.CreateListingAsync(client, "Developer", "Build software", []);
        factory.Llm.Respond = () => Reply("""{"bullets":[]}""");

        var result = await AnalyzeAsync(client, listingId, "EXPERIENCE", entryId, "CONCISE");

        Assert.Equal("SUCCEEDED", result.GetProperty("status").GetString());
        Assert.Contains("Write at most 5 CV bullets for this experience", factory.Llm.LastMessages[0].Text);
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
                    ? $$"""{"bullets":[{"id":{{bulletIds[0]}}}],"paragraph":"Built a payments API."}"""
                    : "{\"bullets\":[]}"
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
    public async Task ExternalPrompt_ConciseAsksForWrittenBulletsAndCaps()
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
        Assert.Contains("raw notes, not a finished CV", instructions);
        Assert.DoesNotContain("raw notes, not a finished CV", detailed);
        Assert.Contains("at most 5 bullets for the newest experience", instructions);
        Assert.Contains("every requirement the profile supports is shown", instructions);
        Assert.Contains("The summary and the CV body must agree", detailed);
        Assert.Contains("\"template\": \"CONCISE\"", instructions);
        Assert.Contains("at most 220 characters", instructions);
        Assert.DoesNotContain("full text, not shortened", instructions);
        Assert.Contains("full text, not shortened", detailed);
        Assert.Contains("Merge related bullets of the same entry", instructions);
        Assert.Contains("reword it with the listing's term", detailed);
        Assert.Contains("\"template\": \"DETAILED\"", detailed);
        Assert.Contains("no em dashes", detailed);
        foreach (var prompt in new[] { instructions, detailed }.Select(p => p.ReplaceLineEndings(" ")))
        {
            Assert.Contains("outside the field of most of the candidate's experience", prompt);
            Assert.Contains("select none and the entry shows only its title and dates", prompt);
        }
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
    public async Task Preview_Concise_KeepsParagraphToTheEntrysFacts()
    {
        var client = await factory.GetOwnerClientAsync();
        var (entryId, bulletIds) = await AddExperienceAsync(client, "Paragraph Preview", "2020-01-01", null,
            "Took over a stalled client project, handling user discovery and requirements", "Built internal web services");
        var listingId = await TailoredCvAnalysisTests.CreateListingAsync(client, "Barista", "Serve coffee", []);

        async Task<string?> ParagraphAsync(string paragraph) => (await client.GraphQLAsync(Preview, new
            {
                listingId, template = "CONCISE",
                json = Candidate(entryId, paragraph, (bulletIds[0], "Took over a stalled client project")),
            })).GetProperty("previewTailoredCvDraft").GetProperty("candidate").GetProperty("content")
            .GetProperty("experiences")[0].GetProperty("paragraph").GetString();

        Assert.Equal("Took over a stalled client project and handled user discovery.",
            await ParagraphAsync("Took over a stalled client project and handled user discovery."));
        // The unselected bullet still describes the role.
        Assert.Equal("Built internal web services and took over a stalled client project.",
            await ParagraphAsync("Built internal web services and took over a stalled client project."));
        // Plain new words are fine in an overview; a number the entry never states is not.
        Assert.Null(await ParagraphAsync("Ran the café for 5 years and trained new baristas."));
    }

    [Fact]
    public async Task Preview_Detailed_KeepsListingWordingAndFallsBackOnAlteredText()
    {
        var client = await factory.GetOwnerClientAsync();
        var (entryId, bulletIds) = await AddExperienceAsync(client, "Tailored Employer", "2020-01-01", null,
            TeamsBullet, "Designed and built internal web services");
        var listingId = await TailoredCvAnalysisTests.CreateListingAsync(client, "Frontend developer", TeamsListing, []);
        const string tailored =
            "Streamlined development process from start to finish within cross-functional product, design, and mobile teams, making it ~30% faster with a more consistent UI.";
        var json = Candidate(entryId, "Worked within cross-functional teams on internal web services.",
            (bulletIds[0], tailored), (bulletIds[1], "Architected a cloud platform serving 2 million users"));

        var payload = (await client.GraphQLAsync(Preview, new { listingId, json, template = "DETAILED" }))
            .GetProperty("previewTailoredCvDraft");

        Assert.Equal(JsonValueKind.Null, payload.GetProperty("errors").ValueKind);
        var experience = payload.GetProperty("candidate").GetProperty("content").GetProperty("experiences")[0];
        var texts = experience.GetProperty("bullets").EnumerateArray().Select(b => b.GetProperty("text").GetString()).ToList();
        Assert.Equal([tailored, "Designed and built internal web services"], texts);
        // A DETAILED paragraph may rephrase freely.
        Assert.Equal("Worked within cross-functional teams on internal web services.",
            experience.GetProperty("paragraph").GetString());
    }

    [Theory]
    [InlineData("CONCISE")]
    [InlineData("DETAILED")]
    public async Task Preview_RejectsAnotherUsersBullet(string template)
    {
        var owner = await factory.GetOwnerClientAsync();
        var (entryId, bulletIds) = await AddExperienceAsync(owner, "Owner Employer", "2020-01-01", null, "Built services");
        var guest = await factory.GetGuestClientAsync();
        var listingId = await TailoredCvAnalysisTests.CreateListingAsync(guest, "Developer", "Build services", []);

        var payload = (await guest.GraphQLAsync(Preview,
                new { listingId, json = Candidate(entryId, (bulletIds[0], "Built services")), template }))
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

    private const string TeamsBullet =
        "Streamlined development process from start to finish with product, design, and mobile teams, making it ~30% faster with a more consistent UI.";
    private const string TeamsListing =
        "Working within cross-functional teams, crafting sophisticated design and modern web that directly engage our users and drive business outcomes. Grow revenue 40%.";

    [Theory]
    [InlineData("Streamlined development process from start to finish within cross-functional product, design, and mobile teams, making it ~30% faster with a more consistent UI.", true)]
    [InlineData("Streamlined development process within cross-functional product, design, and mobile teams, making it ~40% faster.", false)]
    public void KeepsSourceWords_AllowsListingTermsButNotListingNumbers(string text, bool expected) =>
        Assert.Equal(expected, TailoredCvWording.KeepsSourceWords(text, [TeamsBullet],
            TailoredCvWording.ListingText("Frontend developer", TeamsListing, [])));

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

    private static string Candidate(int entryId, params (int Id, string Text)[] bullets) =>
        Candidate(entryId, null, bullets);

    private static string Candidate(int entryId, string? paragraph, params (int Id, string Text)[] bullets) => JsonSerializer.Serialize(new
    {
        schemaVersion = 1,
        content = new
        {
            experiences = new[] { new
            {
                sourceEntryId = entryId, employer = "Employer", title = "Developer", paragraph,
                bullets = bullets.Select(b => new { sourceBulletId = b.Id, text = b.Text }).ToArray(),
            } },
        },
    });

    private static ChatResponse Reply(string json) => new(new ChatMessage(ChatRole.Assistant, json));
}
