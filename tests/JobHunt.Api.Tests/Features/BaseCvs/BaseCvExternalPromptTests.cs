using System.Net.Http.Json;
using System.Text.Json;
using JobHunt.Api.Features.BaseCvs;
using JobHunt.Api.Tests.Features.Listings;
using JobHunt.Api.Tests.Features.Profiles;

namespace JobHunt.Api.Tests.Features.BaseCvs;

public class BaseCvExternalPromptTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Prompt = """
        query ($language: Language!, $targetRole: String!, $notes: String, $template: TailoredCvTemplate, $rewrite: Boolean! = false, $purpose: BaseCvPurpose! = ROLE) {
          baseCvExternalPrompt(language: $language, targetRole: $targetRole, notes: $notes, template: $template, rewrite: $rewrite, purpose: $purpose) { version instructions }
        }
        """;
    private const string Preview = """
        mutation ($language: Language!, $json: String!, $template: TailoredCvTemplate, $rewrite: Boolean! = false) {
          previewBaseCv(input: { language: $language, json: $json, template: $template, rewrite: $rewrite }) {
            candidate { content { language template fullName email headline experiences { paragraph bullets { text sourceBulletId } } } }
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
        Assert.Contains("aim at the role in general", instructions);
        Assert.DoesNotContain("open application", instructions);
        // A base CV has no listing, so no listing-only rule (requirements, gaps, language demands) is sent.
        Assert.DoesNotContain("listing", instructions);
        Assert.Contains("Essential rules", instructions);
        Assert.Contains("foundations of the stack", instructions);
        Assert.Contains("about 25-30 skills", instructions);
        Assert.DoesNotContain("Frontend developer | React and TypeScript", instructions);
        Assert.Contains("Reminder:", instructions[instructions.IndexOf("</profile_json>", StringComparison.Ordinal)..]);
        Assert.Contains("Aim at fintech product teams", instructions[instructions.IndexOf("</profile_json>", StringComparison.Ordinal)..]);
        Assert.Contains("use experienceMonths", instructions);
        Assert.Contains("\"experienceMonths\":", instructions);
        Assert.Contains("arbeidstrening", instructions);
        Assert.Contains("copy the bullet exactly as supplied", instructions);
        Assert.DoesNotContain("combine it with", instructions);
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

        Assert.Contains("Shorten each selected bullet by cutting words", instructions);
        // A base CV has no listing to take terms from.
        Assert.DoesNotContain("listing's term", instructions);
        Assert.Contains("combine it with", instructions);
        Assert.DoesNotContain("at most 5 bullets", instructions);
        Assert.Contains("shorten older and less relevant roles", instructions);
        Assert.Contains("about 15-20 skills", instructions);
        Assert.Contains("short paragraph of at most 2 sentences", instructions);
        Assert.Contains("outside the field of most of the candidate's experience", instructions.ReplaceLineEndings(" "));
        Assert.Contains("\"template\": \"CONCISE\"", instructions);
        Assert.Contains("\"language\" to \"NORWEGIAN\"", instructions);
    }

    [Theory]
    [InlineData("CONCISE")]
    [InlineData("DETAILED")]
    public async Task Prompt_Rewrite_LetsNotesGuideThePlainWording(string template)
    {
        var client = await factory.GetOwnerClientAsync();

        var instructions = (await client.GraphQLAsync(Prompt, new
        {
            language = "NORWEGIAN", targetRole = "Barista", notes = "Explain IT work in plain words", template, rewrite = true,
        })).GetProperty("baseCvExternalPrompt").GetProperty("instructions").GetString()!.ReplaceLineEndings(" ");

        Assert.Contains("They also guide how to word the summary, bullets, and paragraphs", instructions);
        Assert.Contains("rewrite it in plain words", instructions);
        Assert.Contains("names a number or skill its entry's bullets do not state", instructions);
        Assert.DoesNotContain("shorten it by cutting words", instructions);
        Assert.DoesNotContain("copy the bullet exactly as supplied", instructions);
        Assert.DoesNotContain("at most 2 small linking words", instructions);
        Assert.Contains("Translate IT entries into everyday words rather than dropping them", instructions);
        Assert.Contains("select at least its most relevant one, rewritten in plain words", instructions);
        Assert.DoesNotContain("Prefer no bullet over one that only makes sense to IT people", instructions);
        Assert.DoesNotContain("then select none and the entry shows only its title and dates", instructions);
    }

    [Fact]
    public async Task Prompt_OpenApplication_AimsAtAMarket_AndNotesWidenScope()
    {
        var client = await factory.GetOwnerClientAsync();

        var instructions = (await client.GraphQLAsync(Prompt, new
        {
            language = "ENGLISH", targetRole = "Frontend-leaning developer", notes = "Also show backend capabilities",
            template = (string?)null, purpose = "OPEN_APPLICATION",
        })).GetProperty("baseCvExternalPrompt").GetProperty("instructions").GetString()!.ReplaceLineEndings(" ");

        Assert.Contains("\"role\":\"Frontend-leaning developer\"", instructions);
        Assert.Contains("open applications", instructions);
        Assert.Contains("no vacancy and no requirements", instructions);
        Assert.Contains("count as relevant", instructions);
        // Notes outrank the preferences, and may ask for an honest transition line in the summary.
        Assert.Contains("follow the notes", instructions);
        Assert.Contains("transition", instructions);
        Assert.Contains("never null", instructions);
        Assert.DoesNotContain("listing", instructions);
        Assert.DoesNotContain("keep only one or two bullets", instructions);
        Assert.Contains("copy the bullet exactly as supplied", instructions);
        Assert.DoesNotContain("aim at the role in general", instructions);
        Assert.DoesNotContain("read this target", instructions);
    }

    [Theory]
    [InlineData("DETAILED", false, "copy the bullet exactly as supplied")]
    [InlineData("CONCISE", false, "combine it with")]
    [InlineData("DETAILED", true, "Translate IT entries into everyday words rather than dropping them")]
    [InlineData("CONCISE", true, "rewrite it in plain words")]
    public async Task Prompt_OpenApplication_KeepsTemplateAndRewrite(string template, bool rewrite, string expected)
    {
        var client = await factory.GetOwnerClientAsync();

        var instructions = (await client.GraphQLAsync(Prompt, new
        {
            language = "NORWEGIAN", targetRole = "Customer-facing roles", notes = (string?)null, template, rewrite,
            purpose = "OPEN_APPLICATION",
        })).GetProperty("baseCvExternalPrompt").GetProperty("instructions").GetString()!.ReplaceLineEndings(" ");

        Assert.Contains(expected, instructions);
        Assert.Contains("outside the field of most of the candidate's experience", instructions);
        Assert.Contains($"\"template\": \"{template}\"", instructions);
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
    public async Task Preview_Concise_KeepsCopiedAndCombinedBullets_AndAddsProfileContact()
    {
        var client = await factory.GetOwnerClientAsync();
        await client.GraphQLAsync("""
            mutation { updateProfile(input: { fullName: "Base Owner", email: "base@example.com", hobbies: [], links: [] }) { profile { id } } }
            """);
        var (entryId, bulletIds) = await AddExperienceAsync(client, "Combine Employer",
            "Built the checkout in Vue", "Wrote deployment scripts", "Maintained the build pipeline");
        var combined = "Wrote deployment scripts and maintained the build pipeline";
        var json = Candidate(entryId, (bulletIds[0], "Built the checkout in Vue"), (bulletIds[1], combined));

        var payload = (await client.GraphQLAsync(Preview, new { language = "ENGLISH", json, template = "CONCISE" }))
            .GetProperty("previewBaseCv");

        Assert.Equal(JsonValueKind.Null, payload.GetProperty("errors").ValueKind);
        var content = payload.GetProperty("candidate").GetProperty("content");
        Assert.Equal("ENGLISH", content.GetProperty("language").GetString());
        Assert.Equal("CONCISE", content.GetProperty("template").GetString());
        var bullets = content.GetProperty("experiences")[0].GetProperty("bullets");
        Assert.Equal("Built the checkout in Vue", bullets[0].GetProperty("text").GetString());
        Assert.Equal(combined, bullets[1].GetProperty("text").GetString());
        Assert.Equal(bulletIds[1], bullets[1].GetProperty("sourceBulletId").GetInt32());
        Assert.Equal("Base Owner", content.GetProperty("fullName").GetString());
        Assert.Equal("base@example.com", content.GetProperty("email").GetString());
    }

    [Fact]
    public async Task Preview_Detailed_RejectsChangedOrCombinedBulletText()
    {
        var client = await factory.GetOwnerClientAsync();
        var (entryId, bulletIds) = await AddExperienceAsync(client, "Exact Employer",
            "Wrote deployment scripts", "Maintained the build pipeline");
        var json = Candidate(entryId, (bulletIds[0], "Wrote deployment scripts and maintained the build pipeline"));

        var payload = (await client.GraphQLAsync(Preview, new { language = "ENGLISH", json, template = (string?)null }))
            .GetProperty("previewBaseCv");

        Assert.Equal("InvalidTailoredCvDraftError", payload.GetProperty("errors")[0].GetProperty("__typename").GetString());
        Assert.Contains("text must match the source bullet", payload.GetProperty("errors")[0].GetProperty("message").GetString());
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

    [Theory]
    [InlineData("CONCISE", "Wrote deployment scripts and served coffee to customers in the café.", null)]
    [InlineData("CONCISE", "Wrote and maintained deployment scripts and the build pipeline.", "Wrote and maintained deployment scripts and the build pipeline.")]
    [InlineData("DETAILED", "Wrote deployment scripts and served coffee to customers in the café.", "Wrote deployment scripts and served coffee to customers in the café.")]
    public async Task Preview_ConciseParagraph_MustKeepTheEntrysWords(string template, string paragraph, string? expected)
    {
        var client = await factory.GetOwnerClientAsync();
        var (entryId, bulletIds) = await AddExperienceAsync(client, "Paragraph Employer",
            "Wrote deployment scripts", "Maintained the build pipeline");
        var json = Candidate(entryId, paragraph, (bulletIds[0], "Wrote deployment scripts"));

        var experience = (await client.GraphQLAsync(Preview, new { language = "ENGLISH", json, template }))
            .GetProperty("previewBaseCv").GetProperty("candidate").GetProperty("content").GetProperty("experiences")[0];

        Assert.Equal(expected, experience.GetProperty("paragraph").GetString());
    }

    [Fact]
    public async Task Preview_Rewrite_KeepsPlainWording_ButNotNewNumbersOrSkills()
    {
        var client = await factory.GetOwnerClientAsync();
        var (entryId, bulletIds) = await AddExperienceAsync(client, "Rewrite Employer", ["Svelte"],
            "Maintained 3 Craft CMS applications built with React", "Trained 2 interns", "Fixed bugs in Node.js REST APIs");
        var plain = "Looked after 3 websites and kept them running";
        var json = Candidate(entryId, "Looked after websites and kept them running for the people who use them.",
            (bulletIds[0], plain), (bulletIds[1], "Taught 5 new colleagues"), (bulletIds[2], "Fixed errors in Svelte apps"));

        var experience = (await client.GraphQLAsync(Preview, new { language = "ENGLISH", json, template = "CONCISE", rewrite = true }))
            .GetProperty("previewBaseCv").GetProperty("candidate").GetProperty("content").GetProperty("experiences")[0];

        var bullets = experience.GetProperty("bullets");
        Assert.Equal(plain, bullets[0].GetProperty("text").GetString());
        Assert.Equal("Trained 2 interns", bullets[1].GetProperty("text").GetString());
        Assert.Equal("Fixed bugs in Node.js REST APIs", bullets[2].GetProperty("text").GetString());
        Assert.Equal("Looked after websites and kept them running for the people who use them.",
            experience.GetProperty("paragraph").GetString());
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

    private static Task<(int EntryId, List<int> BulletIds)> AddExperienceAsync(HttpClient client, string employer, params string[] bullets) =>
        AddExperienceAsync(client, employer, [], bullets);

    private static async Task<(int EntryId, List<int> BulletIds)> AddExperienceAsync(HttpClient client, string employer,
        string[] skills, params string[] bullets)
    {
        var entryId = (await ProfileEntryTests.AddExperienceAsync(client, employer: ListingTests.Unique(employer), skills: skills))
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

    private static string Candidate(int entryId, params (int? Id, string Text)[] bullets) =>
        Candidate(entryId, null, bullets);

    private static string Candidate(int entryId, string? paragraph, params (int? Id, string Text)[] bullets) => JsonSerializer.Serialize(new
    {
        schemaVersion = 1,
        content = new
        {
            experiences = new[] { new
            {
                sourceEntryId = entryId, employer = "Employer", title = "Developer", startMonth = "2020-01-01", endMonth = (string?)null, paragraph,
                bullets = bullets.Select(b => new { sourceBulletId = b.Id, text = b.Text }),
            } },
        },
    });
}
