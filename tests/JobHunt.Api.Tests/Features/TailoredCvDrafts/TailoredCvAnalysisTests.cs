using System.Text.Json;
using System.Net;
using JobHunt.Api.Data;
using JobHunt.Api.Features.TailoredCvDrafts;
using JobHunt.Api.Features.Profiles;
using JobHunt.Api.Tests.Features.Profiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.AI;

namespace JobHunt.Api.Tests.Features.TailoredCvDrafts;

public class TailoredCvAnalysisTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Analyze = """
        mutation ($listingId: Int!, $section: TailoredCvSection!, $sourceEntryId: Int) {
          analyzeTailoredCvSection(input: { listingId: $listingId, section: $section, sourceEntryId: $sourceEntryId }) {
            result { status section sourceEntryId selectedBulletIds selectedSkillIds summary headline paragraph technologies error }
            errors { __typename ... on Error { message } }
          }
        }
        """;
    private const string AnalyzeAll = """
        mutation ($listingId: Int!) {
          analyzeAllTailoredCv(input: { listingId: $listingId }) {
            analysis { results { status section sourceEntryId selectedBulletIds selectedSkillIds summary paragraph error } }
            errors { __typename ... on Error { message } }
          }
        }
        """;
    private const string PreviewExternal = """
        mutation ($listingId: Int!, $json: String!) {
          previewTailoredCvDraft(input: { listingId: $listingId, json: $json }) {
            candidate { schemaVersion content { headline summary experiences { sourceEntryId employer title bullets { sourceBulletId text } } skills { sourceSkillId name level } } }
            errors { __typename ... on Error { message } }
          }
        }
        """;

    [Fact]
    public async Task ExperienceAnalysis_UsesOnlyRequestedSourceAndReturnsSelectedBulletIds()
    {
        var client = await factory.GetOwnerClientAsync();
        var experience = await ProfileEntryTests.AddExperienceAsync(client, employer: "Allowed Employer", skills: ["React"]);
        var entryId = experience.GetProperty("id").GetInt32();
        var bulletData = await client.GraphQLAsync("""
            mutation ($entryId: Int!) {
              addProfileBullet(input: { kind: EXPERIENCE, entryId: $entryId, text: "Built a React application" }) {
                profileBullet { id }
              }
            }
            """, new { entryId });
        var bulletId = bulletData.GetProperty("addProfileBullet").GetProperty("profileBullet").GetProperty("id").GetInt32();
        await AddPrivateProfileDetailsAsync(client);
        var listingId = await CreateListingAsync(client, "React platform", "Build React services", ["React"]);
        factory.Llm.Respond = () => new ChatResponse(new ChatMessage(ChatRole.Assistant,
            $$"""{"selectedBulletIds":[{{bulletId}}],"paragraph":"Built the React service and improved reliability."}"""));

        var payload = await AnalyzeAsync(client, listingId, "EXPERIENCE", entryId);

        var result = payload.GetProperty("result");
        Assert.Equal("SUCCEEDED", result.GetProperty("status").GetString());
        Assert.Equal(new[] { bulletId }, result.GetProperty("selectedBulletIds").EnumerateArray().Select(v => v.GetInt32()));
        Assert.Equal("Built the React service and improved reliability.", result.GetProperty("paragraph").GetString());
        // No technologySkillIds in the answer: no suggestion, so the UI keeps the entry's list.
        Assert.Equal(JsonValueKind.Null, result.GetProperty("technologies").ValueKind);
        var prompt = string.Join("\n", factory.Llm.LastMessages.Select(m => m.Text));
        Assert.Contains("Allowed Employer", prompt);
        Assert.Contains("Build React services", prompt);
        Assert.DoesNotContain("PRIVATE_", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("Allowed Employer", prompt.Split("<cv_context>")[0], StringComparison.Ordinal);
        Assert.Equal(ChatResponseFormat.Json, factory.Llm.LastOptions!.ResponseFormat);
    }

    [Fact]
    public async Task ProjectAnalysis_UsesOnlyRequestedProjectAndRejectsOtherUsersProject()
    {
        var client = await factory.GetOwnerClientAsync();
        var projectData = await client.GraphQLAsync("""
            mutation { addProfileProject(input: { name: "LLM job tracker", skillNames: ["Microsoft.Extensions.AI"] }) {
              profileProject { id }
            } }
            """);
        var entryId = projectData.GetProperty("addProfileProject").GetProperty("profileProject").GetProperty("id").GetInt32();
        var bulletData = await client.GraphQLAsync("""
            mutation ($entryId: Int!) {
              addProfileBullet(input: { kind: PROJECT, entryId: $entryId, text: "Parsed job listings with an LLM" }) {
                profileBullet { id }
              }
            }
            """, new { entryId });
        var bulletId = bulletData.GetProperty("addProfileBullet").GetProperty("profileBullet").GetProperty("id").GetInt32();
        var listingId = await CreateListingAsync(client, "AI developer", "Build AI-powered features", []);
        factory.Llm.Respond = () => new ChatResponse(new ChatMessage(ChatRole.Assistant,
            $$"""{"selectedBulletIds":[{{bulletId}}],"paragraph":"Parsed job listings with an LLM."}"""));

        var result = (await AnalyzeAsync(client, listingId, "PROJECT", entryId)).GetProperty("result");

        Assert.Equal("SUCCEEDED", result.GetProperty("status").GetString());
        Assert.Equal(new[] { bulletId }, result.GetProperty("selectedBulletIds").EnumerateArray().Select(v => v.GetInt32()));
        var prompt = string.Join("\n", factory.Llm.LastMessages.Select(m => m.Text));
        Assert.Contains("LLM job tracker", prompt);
        Assert.Contains("AI-powered features", prompt);

        var guest = await factory.GetGuestClientAsync();
        var guestListingId = await CreateListingAsync(guest, "Developer", "Build software", []);
        var foreign = await guest.GraphQLAsync(Analyze, new { listingId = guestListingId, section = "PROJECT", sourceEntryId = entryId });
        Assert.Equal("ProfileEntryNotFoundError", foreign.GetProperty("analyzeTailoredCvSection")
            .GetProperty("errors")[0].GetProperty("__typename").GetString());
    }

    [Fact]
    public async Task ModelCannotSelectABulletOutsideTheRequestedExperience()
    {
        var client = await factory.GetOwnerClientAsync();
        var experience = await ProfileEntryTests.AddExperienceAsync(client, employer: "No bullet", skills: []);
        var listingId = await CreateListingAsync(client, "Developer", "Build software", []);
        factory.Llm.Respond = () => new ChatResponse(new ChatMessage(ChatRole.Assistant,
            "{\"selectedBulletIds\":[2147483647]}"));

        var payload = await AnalyzeAsync(client, listingId, "EXPERIENCE", experience.GetProperty("id").GetInt32());

        var result = payload.GetProperty("result");
        Assert.Equal("FAILED", result.GetProperty("status").GetString());
        Assert.Empty(result.GetProperty("selectedBulletIds").EnumerateArray());
        Assert.NotNull(result.GetProperty("error").GetString());
    }

    [Fact]
    public async Task EducationAnalysis_UsesOnlyRequestedSourceAndReturnsSelectedBulletIds()
    {
        var client = await factory.GetOwnerClientAsync();
        var educationData = await client.GraphQLAsync("""
            mutation { addProfileEducation(input: { school: "Allowed School", startMonth: "2015-08-01", endMonth: "2020-06-01" }) {
              profileEducation { id }
            } }
            """);
        var entryId = educationData.GetProperty("addProfileEducation").GetProperty("profileEducation").GetProperty("id").GetInt32();
        var bulletData = await client.GraphQLAsync("""
            mutation ($entryId: Int!) {
              addProfileBullet(input: { kind: EDUCATION, entryId: $entryId, text: "Graduated with honors" }) {
                profileBullet { id }
              }
            }
            """, new { entryId });
        var bulletId = bulletData.GetProperty("addProfileBullet").GetProperty("profileBullet").GetProperty("id").GetInt32();
        var listingId = await CreateListingAsync(client, "Developer", "Build software", []);
        factory.Llm.Respond = () => new ChatResponse(new ChatMessage(ChatRole.Assistant,
            $$"""{"selectedBulletIds":[{{bulletId}}],"paragraph":"Graduated with honors."}"""));

        var payload = await AnalyzeAsync(client, listingId, "EDUCATION", entryId);

        var result = payload.GetProperty("result");
        Assert.Equal("SUCCEEDED", result.GetProperty("status").GetString());
        Assert.Equal("Graduated with honors.", result.GetProperty("paragraph").GetString());
        Assert.Equal(new[] { bulletId }, result.GetProperty("selectedBulletIds").EnumerateArray().Select(v => v.GetInt32()));
        Assert.Contains("Allowed School", string.Join("\n", factory.Llm.LastMessages.Select(m => m.Text)));
        Assert.Contains("always select at least its most relevant one", factory.Llm.LastMessages[0].Text);
        Assert.Contains("never drop them", factory.Llm.LastMessages[0].Text);
    }

    [Fact]
    public async Task SkillsAnalysis_KeepsRelevanceOrder()
    {
        var client = await factory.GetGuestClientAsync();
        async Task<int> SkillAsync(string name) => (await client.GraphQLAsync("""
            mutation ($name: String!) { setProfileSkill(input: { name: $name, level: 4 }) { profileSkill { skill { id } } } }
            """, new { name })).GetProperty("setProfileSkill").GetProperty("profileSkill")
            .GetProperty("skill").GetProperty("id").GetInt32();
        var first = await SkillAsync("Order Alpha");
        var second = await SkillAsync("Order Beta");
        var listingId = await CreateListingAsync(client, "Ordered developer", "Needs Beta, then Alpha", ["Order Beta"]);
        factory.Llm.Respond = () => new ChatResponse(new ChatMessage(ChatRole.Assistant,
            $$"""{"selectedSkillIds":[{{second}},{{first}}]}"""));

        var result = (await AnalyzeAsync(client, listingId, "SKILLS", null)).GetProperty("result");

        Assert.Equal(new[] { second, first }, result.GetProperty("selectedSkillIds").EnumerateArray().Select(v => v.GetInt32()));
        Assert.Contains("most relevant first", factory.Llm.LastMessages[0].Text);
        Assert.Contains("\"usedInWork\":false,\"usedInProjects\":false", factory.Llm.LastMessages[1].Text);
    }

    [Fact]
    public async Task SkillsAnalysis_ReturnsOnlyKnownProfileSkillIds()
    {
        var client = await factory.GetGuestClientAsync();
        var skillData = await client.GraphQLAsync("""
            mutation { setProfileSkill(input: { name: "Vue", level: 4 }) { profileSkill { skill { id } } } }
            """);
        var skillId = skillData.GetProperty("setProfileSkill").GetProperty("profileSkill")
            .GetProperty("skill").GetProperty("id").GetInt32();
        var listingId = await CreateListingAsync(client, "Vue developer", "Build Vue apps", ["Vue"]);
        factory.Llm.Respond = () => new ChatResponse(new ChatMessage(ChatRole.Assistant,
            $$"""{"selectedSkillIds":[{{skillId}}]}"""));

        var result = (await AnalyzeAsync(client, listingId, "SKILLS", null)).GetProperty("result");

        Assert.Equal("SUCCEEDED", result.GetProperty("status").GetString());
        Assert.Equal(skillId, result.GetProperty("selectedSkillIds")[0].GetInt32());
    }

    [Fact]
    public async Task SummaryAnalysis_ProducesSeparateListingSpecificSuggestion()
    {
        var client = await factory.GetGuestClientAsync();
        await client.GraphQLAsync("""
            mutation {
              updateProfile(input: { headline: "Frontend developer", summary: "Builds Vue applications", hobbies: [], links: [] }) {
                profile { id }
              }
            }
            """);
        await ProfileEntryTests.AddExperienceAsync(client, employer: "Summary Context Employer", start: "2019-04-01");
        await client.GraphQLAsync("""
            mutation { addProfileEducation(input: { school: "Summary Context School", startMonth: "2015-08-01", endMonth: "2018-06-01" }) {
              profileEducation { id }
            } }
            """);
        var listingId = await CreateListingAsync(client, "Vue developer", "Build Vue applications", ["Vue"]);
        factory.Llm.Respond = () => new ChatResponse(new ChatMessage(ChatRole.Assistant,
            "{\"summary\":\"Frontend developer focused on Vue applications.\",\"headline\":\" Frontend developer | Vue \"}"));

        var result = (await AnalyzeAsync(client, listingId, "SUMMARY", null)).GetProperty("result");

        Assert.Equal("SUCCEEDED", result.GetProperty("status").GetString());
        Assert.Equal("Frontend developer focused on Vue applications.", result.GetProperty("summary").GetString());
        Assert.Equal("Frontend developer | Vue", result.GetProperty("headline").GetString());
        Assert.Contains("Builds Vue applications", string.Join("\n", factory.Llm.LastMessages.Select(m => m.Text)));
        Assert.Contains("\"employer\":\"Summary Context Employer\",\"startMonth\":\"2019-04-01\"", factory.Llm.LastMessages[1].Text);
        Assert.Contains("Summary Context School", factory.Llm.LastMessages[1].Text);
        Assert.Contains("years alone are not", factory.Llm.LastMessages[0].Text);
        Assert.Contains("never claim it", factory.Llm.LastMessages[0].Text);
        Assert.Contains("never relabel them as fullstack", factory.Llm.LastMessages[0].Text);
        Assert.Contains("null keeps the current headline", factory.Llm.LastMessages[0].Text);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("\"   \"")]
    [InlineData("TOO_LONG")]
    public async Task SummaryAnalysis_MissingOrUnusableHeadline_IsNoSuggestion(string headline)
    {
        var client = await factory.GetGuestClientAsync();
        var listingId = await CreateListingAsync(client, "Vue developer", "Build Vue applications", ["Vue"]);
        var value = headline == "TOO_LONG" ? $"\"{new string('a', 201)}\"" : headline;
        factory.Llm.Respond = () => new ChatResponse(new ChatMessage(ChatRole.Assistant,
            $"{{\"summary\":\"Builds Vue applications.\",\"headline\":{value}}}"));

        var result = (await AnalyzeAsync(client, listingId, "SUMMARY", null)).GetProperty("result");

        Assert.Equal("SUCCEEDED", result.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("headline").ValueKind);
    }

    [Fact]
    public async Task SummaryAnalysis_HeadlineOfTheWrongType_IsUnreadable()
    {
        var client = await factory.GetGuestClientAsync();
        var listingId = await CreateListingAsync(client, "Vue developer", "Build Vue applications", ["Vue"]);
        factory.Llm.Respond = () => new ChatResponse(new ChatMessage(ChatRole.Assistant,
            "{\"summary\":\"Builds Vue applications.\",\"headline\":42}"));

        var result = (await AnalyzeAsync(client, listingId, "SUMMARY", null)).GetProperty("result");

        Assert.Equal("FAILED", result.GetProperty("status").GetString());
    }

    [Fact]
    public async Task SummaryAnalysis_NorwegianListing_AsksForNorwegianFromTranslatedSummary()
    {
        var client = await factory.GetGuestClientAsync();
        await client.GraphQLAsync("""
            mutation {
              updateProfile(input: { headline: "Frontend developer", summary: "Builds Vue applications", hobbies: [], links: [] }) {
                profile { id }
              }
            }
            """);
        await client.GraphQLAsync("""
            mutation { setProfileTranslation(input: { language: NORWEGIAN, summary: "Bygger Vue-applikasjoner" }) { profileTranslation { language } } }
            """);
        var listingId = await CreateListingAsync(client, "Vue-utvikler", "Bygg Vue-applikasjoner", ["Vue"], "NORWEGIAN");
        factory.Llm.Respond = () => new ChatResponse(new ChatMessage(ChatRole.Assistant, "{\"summary\":\"Frontendutvikler.\"}"));

        var result = (await AnalyzeAsync(client, listingId, "SUMMARY", null)).GetProperty("result");

        Assert.Equal("SUCCEEDED", result.GetProperty("status").GetString());
        var messages = string.Join("\n", factory.Llm.LastMessages.Select(m => m.Text));
        Assert.Contains("Write in Norwegian (bokmål)", messages);
        Assert.Contains("Bygger Vue-applikasjoner", messages);
        Assert.DoesNotContain("Builds Vue applications", messages);
    }

    [Fact]
    public async Task ExperienceAnalysis_NorwegianListing_SendsTranslatedBulletsAndAsksForNorwegianParagraph()
    {
        var client = await factory.GetGuestClientAsync();
        var experience = await ProfileEntryTests.AddExperienceAsync(client, employer: "Paragraph Employer", skills: []);
        var entryId = experience.GetProperty("id").GetInt32();
        var bulletId = (await client.GraphQLAsync("""
            mutation ($entryId: Int!) {
              addProfileBullet(input: { kind: EXPERIENCE, entryId: $entryId, text: "Built payment services" }) { profileBullet { id } }
            }
            """, new { entryId })).GetProperty("addProfileBullet").GetProperty("profileBullet").GetProperty("id").GetInt32();
        await client.GraphQLAsync("""
            mutation ($bulletId: Int!) {
              setProfileBulletTranslation(input: { bulletId: $bulletId, language: NORWEGIAN, text: "Bygde betalingstjenester" }) {
                profileBulletTranslation { id }
              }
            }
            """, new { bulletId });
        var listingId = await CreateListingAsync(client, "Utvikler", "Bygg betalingstjenester", [], "NORWEGIAN");
        factory.Llm.Respond = () => new ChatResponse(new ChatMessage(ChatRole.Assistant,
            $$"""{"selectedBulletIds":[{{bulletId}}],"paragraph":"Bygde betalingstjenester."}"""));

        var result = (await AnalyzeAsync(client, listingId, "EXPERIENCE", entryId)).GetProperty("result");

        Assert.Equal("SUCCEEDED", result.GetProperty("status").GetString());
        Assert.Equal(new[] { bulletId }, result.GetProperty("selectedBulletIds").EnumerateArray().Select(v => v.GetInt32()));
        Assert.Contains("Write the paragraph in Norwegian (bokmål)", factory.Llm.LastMessages[0].Text.ReplaceLineEndings(" "));
        Assert.Contains("Bygde betalingstjenester", factory.Llm.LastMessages[1].Text);
        Assert.DoesNotContain("Built payment services", factory.Llm.LastMessages[1].Text);
    }

    [Fact]
    public async Task ExperienceAnalysis_RequestedEnglish_OnNorwegianListing_SendsOriginalBullets()
    {
        var client = await factory.GetGuestClientAsync();
        var experience = await ProfileEntryTests.AddExperienceAsync(client, employer: "Switch Employer", skills: []);
        var entryId = experience.GetProperty("id").GetInt32();
        var bulletId = (await client.GraphQLAsync("""
            mutation ($entryId: Int!) {
              addProfileBullet(input: { kind: EXPERIENCE, entryId: $entryId, text: "Built billing services" }) { profileBullet { id } }
            }
            """, new { entryId })).GetProperty("addProfileBullet").GetProperty("profileBullet").GetProperty("id").GetInt32();
        await client.GraphQLAsync("""
            mutation ($bulletId: Int!) {
              setProfileBulletTranslation(input: { bulletId: $bulletId, language: NORWEGIAN, text: "Bygde fakturatjenester" }) {
                profileBulletTranslation { id }
              }
            }
            """, new { bulletId });
        var listingId = await CreateListingAsync(client, "Utvikler", "Bygg fakturatjenester", [], "NORWEGIAN");
        factory.Llm.Respond = () => new ChatResponse(new ChatMessage(ChatRole.Assistant,
            $$"""{"selectedBulletIds":[{{bulletId}}],"paragraph":"Built billing services."}"""));

        var result = (await client.GraphQLAsync("""
            mutation ($listingId: Int!, $sourceEntryId: Int) {
              analyzeTailoredCvSection(input: { listingId: $listingId, section: EXPERIENCE, sourceEntryId: $sourceEntryId, language: ENGLISH }) {
                result { status }
              }
            }
            """, new { listingId, sourceEntryId = entryId })).GetProperty("analyzeTailoredCvSection").GetProperty("result");

        Assert.Equal("SUCCEEDED", result.GetProperty("status").GetString());
        Assert.DoesNotContain("Norwegian (bokmål)", factory.Llm.LastMessages[0].Text.ReplaceLineEndings(" "));
        Assert.Contains("Built billing services", factory.Llm.LastMessages[1].Text);
        Assert.DoesNotContain("Bygde fakturatjenester", factory.Llm.LastMessages[1].Text);
    }

    [Fact]
    public async Task ExperienceAnalysis_ReturnsChosenTechnologiesInTheDraftLanguage_OnlyFromTheEntry()
    {
        var client = await factory.GetGuestClientAsync();
        var g = Guid.NewGuid().ToString("N");
        var experience = await ProfileEntryTests.AddExperienceAsync(client, employer: $"Tech {g}", skills: [$"Unit{g}", $"Docker{g}"]);
        var entryId = experience.GetProperty("id").GetInt32();
        await ProfileEntryTests.AddExperienceAsync(client, employer: $"Other {g}", skills: [$"Other{g}"]);
        var skills = (await client.GraphQLAsync("{ profile { experiences { skills { id name } } } }"))
            .GetProperty("profile").GetProperty("experiences").EnumerateArray()
            .SelectMany(e => e.GetProperty("skills").EnumerateArray()).DistinctBy(s => s.GetProperty("id").GetInt32())
            .ToDictionary(s => s.GetProperty("name").GetString()!, s => s.GetProperty("id").GetInt32());
        await client.GraphQLAsync("""
            mutation ($skillId: Int!, $name: String!) {
              setSkillTranslation(input: { skillId: $skillId, language: NORWEGIAN, name: $name }) { skill { id } }
            }
            """, new { skillId = skills[$"Unit{g}"], name = $"Enhet{g}" });
        var otherSkillId = skills[$"Other{g}"];
        var listingId = await CreateListingAsync(client, "Utvikler", "Bygg tjenester", [], "NORWEGIAN");

        factory.Llm.Respond = () => new ChatResponse(new ChatMessage(ChatRole.Assistant,
            $$"""{"selectedBulletIds":[],"paragraph":null,"technologySkillIds":[{{skills[$"Unit{g}"]}}]}"""));
        var result = (await AnalyzeAsync(client, listingId, "EXPERIENCE", entryId)).GetProperty("result");

        Assert.Equal("SUCCEEDED", result.GetProperty("status").GetString());
        Assert.Equal([$"Enhet{g}"], result.GetProperty("technologies").EnumerateArray().Select(t => t.GetString()));
        Assert.Contains("technologySkillIds", factory.Llm.LastMessages[0].Text);
        Assert.Contains($"Enhet{g}", factory.Llm.LastMessages[1].Text);

        factory.Llm.Respond = () => new ChatResponse(new ChatMessage(ChatRole.Assistant,
            $$"""{"selectedBulletIds":[],"paragraph":null,"technologySkillIds":[{{otherSkillId}}]}"""));
        var foreign = (await AnalyzeAsync(client, listingId, "EXPERIENCE", entryId)).GetProperty("result");

        Assert.Equal("FAILED", foreign.GetProperty("status").GetString());
    }

    [Fact]
    public async Task ExternalPreview_FillsMissingProjectKindAndTechnologiesFromTheSourceProject()
    {
        var client = await factory.GetGuestClientAsync();
        var skill = $"Preview{Guid.NewGuid():N}";
        var projectId = (await client.GraphQLAsync("""
            mutation ($skill: String!) { addProfileProject(input: { name: "Preview kind", kind: PERSONAL, skillNames: [$skill] }) { profileProject { id } } }
            """, new { skill })).GetProperty("addProfileProject").GetProperty("profileProject").GetProperty("id").GetInt32();
        var listingId = await CreateListingAsync(client, "Developer", "Build software", []);
        var json = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            content = new
            {
                projects = new object[]
                {
                    new { sourceEntryId = projectId, name = "Preview kind", bullets = Array.Empty<object>() },
                    new { sourceEntryId = projectId, name = "Preview kind", technologies = Array.Empty<string>(), bullets = Array.Empty<object>() },
                },
            },
        });

        var payload = (await client.GraphQLAsync("""
            mutation ($listingId: Int!, $json: String!) {
              previewTailoredCvDraft(input: { listingId: $listingId, json: $json }) {
                candidate { content { projects { kind technologies } } } errors { __typename }
              }
            }
            """, new { listingId, json })).GetProperty("previewTailoredCvDraft");

        var projects = payload.GetProperty("candidate").GetProperty("content").GetProperty("projects");
        Assert.Equal("PERSONAL", projects[0].GetProperty("kind").GetString());
        Assert.Equal([skill], projects[0].GetProperty("technologies").EnumerateArray().Select(t => t.GetString()));
        // An empty list was the model's choice, not an omission.
        Assert.Equal(0, projects[1].GetProperty("technologies").GetArrayLength());
    }

    [Fact]
    public async Task ExternalPreview_NorwegianListing_AcceptsTranslatedBulletText()
    {
        var client = await factory.GetOwnerClientAsync();
        var experience = await ProfileEntryTests.AddExperienceAsync(client, employer: "Translated Employer", skills: []);
        var entryId = experience.GetProperty("id").GetInt32();
        var bulletId = (await client.GraphQLAsync("""
            mutation ($entryId: Int!) {
              addProfileBullet(input: { kind: EXPERIENCE, entryId: $entryId, text: "Built services" }) { profileBullet { id } }
            }
            """, new { entryId })).GetProperty("addProfileBullet").GetProperty("profileBullet").GetProperty("id").GetInt32();
        await client.GraphQLAsync("""
            mutation ($bulletId: Int!) {
              setProfileBulletTranslation(input: { bulletId: $bulletId, language: NORWEGIAN, text: "Bygde tjenester" }) {
                profileBulletTranslation { id }
              }
            }
            """, new { bulletId });
        var listingId = await CreateListingAsync(client, "Utvikler", "Bygg tjenester", [], "NORWEGIAN");

        var instructions = (await client.GraphQLAsync("""
            query ($listingId: Int!) { tailoredCvExternalPrompt(listingId: $listingId) { instructions } }
            """, new { listingId })).GetProperty("tailoredCvExternalPrompt").GetProperty("instructions").GetString()!;
        Assert.Contains("Bygde tjenester", instructions);
        Assert.Contains("\"NORWEGIAN\"", instructions);

        var json = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            content = new
            {
                experiences = new[] { new
                {
                    sourceEntryId = entryId, employer = "Translated Employer", title = "Utvikler",
                    bullets = new[] { new { sourceBulletId = bulletId, text = "Bygde tjenester" } },
                } },
            },
        });
        var payload = (await client.GraphQLAsync(PreviewExternal, new { listingId, json })).GetProperty("previewTailoredCvDraft");

        Assert.Equal(JsonValueKind.Null, payload.GetProperty("errors").ValueKind);
    }

    [Fact]
    public async Task ExternalPromptAndPreview_RequestedLanguage_OverridesListingAndPastedLanguage()
    {
        var client = await factory.GetOwnerClientAsync();
        var listingId = await CreateListingAsync(client, "Developer", "Build services", [], "ENGLISH");

        var instructions = (await client.GraphQLAsync("""
            query ($listingId: Int!) { tailoredCvExternalPrompt(listingId: $listingId, language: NORWEGIAN) { instructions } }
            """, new { listingId })).GetProperty("tailoredCvExternalPrompt").GetProperty("instructions").GetString()!;
        var json = JsonSerializer.Serialize(new { schemaVersion = 1, content = new { language = "ENGLISH" } });
        var candidate = (await client.GraphQLAsync("""
            mutation ($listingId: Int!, $json: String!) {
              previewTailoredCvDraft(input: { listingId: $listingId, json: $json, language: NORWEGIAN }) {
                candidate { content { language } }
              }
            }
            """, new { listingId, json })).GetProperty("previewTailoredCvDraft").GetProperty("candidate");

        Assert.Contains("\"NORWEGIAN\"", instructions);
        Assert.Equal("NORWEGIAN", candidate.GetProperty("content").GetProperty("language").GetString());
    }

    [Fact]
    public async Task ExternalPrompt_ContainsListingAndProfileSourcesButExcludesPrivateFields()
    {
        var client = await factory.GetOwnerClientAsync();
        var experience = await ProfileEntryTests.AddExperienceAsync(client, employer: "Prompt Employer", skills: ["Go"]);
        var entryId = experience.GetProperty("id").GetInt32();
        var bulletData = await client.GraphQLAsync("""
            mutation ($entryId: Int!) {
              addProfileBullet(input: { kind: EXPERIENCE, entryId: $entryId, text: "Built Go services" }) {
                profileBullet { id }
              }
            }
            """, new { entryId });
        var bulletId = bulletData.GetProperty("addProfileBullet").GetProperty("profileBullet").GetProperty("id").GetInt32();
        await AddPrivateProfileDetailsAsync(client);
        var listingId = await CreateListingAsync(client, "Go developer", "Build Go services", ["Go"]);
        var companyId = (await client.GraphQLAsync("query ($id: Int!) { listingById(id: $id) { company { id } } }", new { id = listingId }))
            .GetProperty("listingById").GetProperty("company").GetProperty("id").GetInt32();
        await client.GraphQLAsync("""
            mutation ($id: Int!) { updateCompanyDetails(input: { id: $id, details: { kind: BROKER, skills: [] } }) { errors { __typename } } }
            """, new { id = companyId });

        var data = await client.GraphQLAsync("""
            query ($listingId: Int!) { tailoredCvExternalPrompt(listingId: $listingId) { version instructions } }
            """, new { listingId });

        var prompt = data.GetProperty("tailoredCvExternalPrompt");
        var instructions = prompt.GetProperty("instructions").GetString()!;
        Assert.Equal(TailoredCvExternalPromptQueries.Version, prompt.GetProperty("version").GetInt32());
        Assert.Contains("Go developer", instructions);
        Assert.Contains("Prompt Employer", instructions);
        Assert.Contains($"\"id\":{bulletId}", instructions);
        Assert.Contains("schemaVersion", instructions);
        Assert.Contains("candidate's own voice", instructions);
        Assert.Contains("sourceBulletId", instructions);
        Assert.Contains("https://tailored-cv-analysis.example/", instructions);
        Assert.Contains("open exactly its url", instructions);
        Assert.Contains("Never search the web for the posting", instructions);
        Assert.Contains("core professional", instructions);
        Assert.Contains("complete timeline", instructions);
        Assert.Contains("never leave paragraph null", instructions);
        Assert.Contains("such as Norwegian", instructions);
        Assert.Contains("every paragraph in that", instructions);
        Assert.Contains("do not turn \"helped\" into \"led\"", instructions);
        Assert.Contains("never claim it", instructions);
        Assert.Contains("\"companyResearch\":{\"kind\":\"BROKER\"", instructions);
        Assert.Contains("CONSULTANCY or BROKER", instructions);
        Assert.Contains("\"headline\": null", instructions);
        Assert.Contains("Set content.headline: one short title line", instructions);
        Assert.Contains("never relabel them as fullstack", instructions);
        Assert.True(instructions.IndexOf("Go developer", StringComparison.Ordinal)
            < instructions.IndexOf("Prompt Employer", StringComparison.Ordinal));
        Assert.DoesNotContain("PRIVATE_", instructions, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExternalPreview_ReturnsReviewedCandidateWithoutSaving()
    {
        var client = await factory.GetOwnerClientAsync();
        var experience = await ProfileEntryTests.AddExperienceAsync(client, employer: "External Employer", skills: []);
        var entryId = experience.GetProperty("id").GetInt32();
        var bulletData = await client.GraphQLAsync("""
            mutation ($entryId: Int!) {
              addProfileBullet(input: { kind: EXPERIENCE, entryId: $entryId, text: "Original source bullet" }) {
                profileBullet { id }
              }
            }
            """, new { entryId });
        var bulletId = bulletData.GetProperty("addProfileBullet").GetProperty("profileBullet").GetProperty("id").GetInt32();
        var listingId = await CreateListingAsync(client, "Developer", "Build software", []);
        var json = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            content = new
            {
                headline = "Frontend developer | Vue",
                summary = "Tailored summary",
                experiences = new[] { new
                {
                    sourceEntryId = entryId, employer = "External Employer", title = "Frontend Developer",
                    startMonth = "2020-01-01", endMonth = (string?)null,
                    bullets = new[] { new { sourceBulletId = (int?)bulletId, text = "Original source bullet" } },
                } },
                educations = Array.Empty<object>(), certifications = Array.Empty<object>(), projects = Array.Empty<object>(),
                skills = Array.Empty<object>(), languages = Array.Empty<object>(),
            },
        });

        var payload = (await client.GraphQLAsync(PreviewExternal, new { listingId, json })).GetProperty("previewTailoredCvDraft");

        Assert.Equal(JsonValueKind.Null, payload.GetProperty("errors").ValueKind);
        var candidate = payload.GetProperty("candidate");
        Assert.Equal(1, candidate.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("Frontend developer | Vue", candidate.GetProperty("content").GetProperty("headline").GetString());
        var selected = candidate.GetProperty("content").GetProperty("experiences")[0];
        Assert.Equal(entryId, selected.GetProperty("sourceEntryId").GetInt32());
        Assert.Equal(bulletId, selected.GetProperty("bullets")[0].GetProperty("sourceBulletId").GetInt32());
        var draft = await client.GraphQLAsync("""
            query ($listingId: Int!) { tailoredCvDraft(listingId: $listingId) { id } }
            """, new { listingId });
        Assert.Equal(JsonValueKind.Null, draft.GetProperty("tailoredCvDraft").ValueKind);
    }

    [Fact]
    public async Task ExternalPreview_RejectsChangedTextForSourceBulletWithoutSaving()
    {
        var client = await factory.GetOwnerClientAsync();
        var experience = await ProfileEntryTests.AddExperienceAsync(client, employer: "External Employer", skills: []);
        var entryId = experience.GetProperty("id").GetInt32();
        var bulletData = await client.GraphQLAsync("""
            mutation ($entryId: Int!) {
              addProfileBullet(input: { kind: EXPERIENCE, entryId: $entryId, text: "Original source bullet" }) {
                profileBullet { id }
              }
            }
            """, new { entryId });
        var bulletId = bulletData.GetProperty("addProfileBullet").GetProperty("profileBullet").GetProperty("id").GetInt32();
        var listingId = await CreateListingAsync(client, "Developer", "Build software", []);
        var json = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            content = new
            {
                experiences = new[] { new
                {
                    sourceEntryId = entryId, employer = "External Employer", title = "Developer",
                    bullets = new[] { new { sourceBulletId = bulletId, text = "Invented achievement" } },
                } },
            },
        });

        var payload = (await client.GraphQLAsync(PreviewExternal, new { listingId, json }))
            .GetProperty("previewTailoredCvDraft");

        Assert.Equal("InvalidTailoredCvDraftError", payload.GetProperty("errors")[0].GetProperty("__typename").GetString());
        Assert.Contains("must match the source bullet", payload.GetProperty("errors")[0].GetProperty("message").GetString());
        Assert.Equal(JsonValueKind.Null, payload.GetProperty("candidate").ValueKind);
        var draft = await client.GraphQLAsync("""
            query ($listingId: Int!) { tailoredCvDraft(listingId: $listingId) { id } }
            """, new { listingId });
        Assert.Equal(JsonValueKind.Null, draft.GetProperty("tailoredCvDraft").ValueKind);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("{\"schemaVersion\":2,\"content\":{}}")]
    [InlineData("{\"schemaVersion\":1,\"content\":{},\"unexpected\":true}")]
    public async Task ExternalPreview_RejectsInvalidJsonAndSchemaWithoutSaving(string json)
    {
        var client = await factory.GetOwnerClientAsync();
        var listingId = await CreateListingAsync(client, "Invalid JSON test", "Build software", []);

        var payload = (await client.GraphQLAsync(PreviewExternal, new { listingId, json })).GetProperty("previewTailoredCvDraft");

        Assert.Equal("InvalidTailoredCvDraftError", payload.GetProperty("errors")[0].GetProperty("__typename").GetString());
        Assert.Equal(JsonValueKind.Null, payload.GetProperty("candidate").ValueKind);
    }

    [Theory]
    [InlineData("{\"schemaVersion\":1,\"content\":{\"experiences\":[null]}}")]
    [InlineData("{\"schemaVersion\":1,\"content\":{\"educations\":[null]}}")]
    [InlineData("{\"schemaVersion\":1,\"content\":{\"certifications\":[null]}}")]
    [InlineData("{\"schemaVersion\":1,\"content\":{\"projects\":[null]}}")]
    [InlineData("{\"schemaVersion\":1,\"content\":{\"skills\":[null]}}")]
    [InlineData("{\"schemaVersion\":1,\"content\":{\"languages\":[null]}}")]
    [InlineData("{\"schemaVersion\":1,\"content\":{\"links\":[null]}}")]
    [InlineData("{\"schemaVersion\":1,\"content\":{\"experiences\":[{\"employer\":\"Example\",\"title\":\"Developer\",\"bullets\":[null]}]}}")]
    public async Task ExternalPreview_RejectsNullCandidateArrayItemsWithoutSaving(string json)
    {
        var client = await factory.GetOwnerClientAsync();
        var listingId = await CreateListingAsync(client, "Null candidate item", "Build software", []);

        var payload = (await client.GraphQLAsync(PreviewExternal, new { listingId, json }))
            .GetProperty("previewTailoredCvDraft");

        Assert.Equal("InvalidTailoredCvDraftError", payload.GetProperty("errors")[0].GetProperty("__typename").GetString());
        Assert.Contains("must not be null", payload.GetProperty("errors")[0].GetProperty("message").GetString());
        Assert.Equal(JsonValueKind.Null, payload.GetProperty("candidate").ValueKind);
        var draft = await client.GraphQLAsync("""
            query ($listingId: Int!) { tailoredCvDraft(listingId: $listingId) { id } }
            """, new { listingId });
        Assert.Equal(JsonValueKind.Null, draft.GetProperty("tailoredCvDraft").ValueKind);
    }

    [Fact]
    public async Task ExternalPreview_RejectsSourceIdsFromAnotherUser()
    {
        var owner = await factory.GetOwnerClientAsync();
        var listingId = await CreateListingAsync(owner, "Owner listing", "Build software", []);
        var guest = await factory.GetGuestClientAsync();
        var foreignExperience = await ProfileEntryTests.AddExperienceAsync(guest, employer: "Guest source", skills: []);
        var foreignEntryId = foreignExperience.GetProperty("id").GetInt32();

        var json = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            content = new
            {
                experiences = new[] { new
                {
                    sourceEntryId = foreignEntryId, employer = "Guest source", title = "Developer",
                    startMonth = "2020-01-01", endMonth = (string?)null, bullets = Array.Empty<object>(),
                } },
            },
        });
        var payload = (await owner.GraphQLAsync(PreviewExternal, new { listingId, json })).GetProperty("previewTailoredCvDraft");

        Assert.Equal("InvalidTailoredCvDraftError", payload.GetProperty("errors")[0].GetProperty("__typename").GetString());
        Assert.Contains("sourceEntryId", payload.GetProperty("errors")[0].GetProperty("message").GetString());
    }

    [Fact]
    public async Task ForeignListingIsNotSentToTheProvider()
    {
        var owner = await factory.GetOwnerClientAsync();
        var listingId = await CreateListingAsync(owner, "Private listing", "Private description", []);
        var guest = await factory.GetGuestClientAsync();
        factory.Llm.Respond = () => throw new InvalidOperationException("The provider must not be called.");

        var result = await AnalyzeAsync(guest, listingId, "SKILLS", null);

        Assert.Equal("ListingNotFoundError", result.GetProperty("errors")[0].GetProperty("__typename").GetString());
    }

    [Fact]
    public async Task ExperienceAnalysis_RequiresAValidSourceEntryBeforeCallingProvider()
    {
        var client = await factory.GetOwnerClientAsync();
        var listingId = await CreateListingAsync(client, "Developer", "Build software", []);
        factory.Llm.Respond = () => throw new InvalidOperationException("The provider must not be called.");

        var result = await AnalyzeAsync(client, listingId, "EXPERIENCE", null);

        Assert.Equal("InvalidTailoredCvAnalysisError", result.GetProperty("errors")[0].GetProperty("__typename").GetString());
    }

    [Fact]
    public async Task OversizedAnalysisContextIsRejectedBeforeCallingProvider()
    {
        var client = await factory.GetOwnerClientAsync();
        var listingId = await CreateListingAsync(client, "Developer", "Build software", []);
        // The API now caps descriptions below this, so only a row from before the cap can get this long.
        using (var scope = factory.Services.CreateScope())
        {
            var description = new string('x', TailoredCvAnalyzer.MaxContextLength + 1);
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Listings.IgnoreQueryFilters()
                .Where(l => l.Id == listingId).ExecuteUpdateAsync(s => s.SetProperty(l => l.Description, description));
        }
        factory.Llm.Respond = () => throw new InvalidOperationException("The provider must not be called.");

        var result = await AnalyzeAsync(client, listingId, "SKILLS", null);

        Assert.Equal("InvalidTailoredCvAnalysisError", result.GetProperty("errors")[0].GetProperty("__typename").GetString());
        Assert.Contains("100000", result.GetProperty("errors")[0].GetProperty("message").GetString());
    }

    [Fact]
    public async Task OversizedModelOutputReturnsFailedCandidate()
    {
        var client = await factory.GetGuestClientAsync();
        var listingId = await CreateListingAsync(client, "Developer", "Build software", []);
        factory.Llm.Respond = () => new ChatResponse(new ChatMessage(ChatRole.Assistant,
            new string('x', TailoredCvAnalyzer.MaxOutputLength + 1)));

        var result = (await AnalyzeAsync(client, listingId, "SUMMARY", null)).GetProperty("result");

        Assert.Equal("FAILED", result.GetProperty("status").GetString());
        Assert.Contains("too long", result.GetProperty("error").GetString());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("summary").ValueKind);
    }

    [Fact]
    public async Task AnalyzeAll_RunsOneCallPerEntryThenSkillsAndSummary_AndLogsEach()
    {
        var client = await factory.GetOwnerClientAsync();
        await ProfileEntryTests.AddExperienceAsync(client, employer: $"Analyze all {Guid.NewGuid()}", skills: []);
        await client.GraphQLAsync("""
            mutation { addProfileEducation(input: { school: "Analyze all school", startMonth: "2015-08-01" }) {
              profileEducation { id }
            } }
            """);
        await client.GraphQLAsync("""
            mutation { addProfileProject(input: { name: "Analyze all project", skillNames: [] }) { profileProject { id } } }
            """);
        var profile = await client.GraphQLAsync("{ profile { experiences { id } educations { id } projects { id } } }");
        var experienceCount = profile.GetProperty("profile").GetProperty("experiences").GetArrayLength();
        var educationCount = profile.GetProperty("profile").GetProperty("educations").GetArrayLength();
        var projectCount = profile.GetProperty("profile").GetProperty("projects").GetArrayLength();
        var listingId = await CreateListingAsync(client, "Full stack developer", "Build web applications", []);
        factory.Llm.Respond = () =>
        {
            var instructions = factory.Llm.LastMessages[0].Text;
            var json = instructions.Contains(" bullet IDs ", StringComparison.Ordinal)
                ? "{\"selectedBulletIds\":[]}"
                : instructions.Contains("profile skill IDs", StringComparison.Ordinal)
                    ? "{\"selectedSkillIds\":[]}" : "{\"summary\":null}";
            return new ChatResponse(new ChatMessage(ChatRole.Assistant, json));
        };
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var usageBefore = await db.LlmUsages.IgnoreQueryFilters()
            .CountAsync(u => u.Operation == TailoredCvAnalyzer.Operation);

        var data = await client.GraphQLAsync(AnalyzeAll, new { listingId });

        var payload = data.GetProperty("analyzeAllTailoredCv");
        Assert.Equal(JsonValueKind.Null, payload.GetProperty("errors").ValueKind);
        var results = payload.GetProperty("analysis").GetProperty("results").EnumerateArray().ToList();
        Assert.Equal(experienceCount + educationCount + projectCount + 2, results.Count);
        Assert.Equal(experienceCount, results.Count(r => r.GetProperty("section").GetString() == "EXPERIENCE"));
        Assert.Equal(educationCount, results.Count(r => r.GetProperty("section").GetString() == "EDUCATION"));
        Assert.Equal(projectCount, results.Count(r => r.GetProperty("section").GetString() == "PROJECT"));
        Assert.Equal("SKILLS", results[^2].GetProperty("section").GetString());
        Assert.Equal("SUMMARY", results[^1].GetProperty("section").GetString());
        Assert.All(results, result => Assert.Equal("SUCCEEDED", result.GetProperty("status").GetString()));
        Assert.Equal(usageBefore + experienceCount + educationCount + projectCount + 2, await db.LlmUsages.IgnoreQueryFilters()
            .CountAsync(u => u.Operation == TailoredCvAnalyzer.Operation));
        Assert.Equal(JsonValueKind.Null, (await client.GraphQLAsync("""
            query ($listingId: Int!) { tailoredCvDraft(listingId: $listingId) { id } }
            """, new { listingId })).GetProperty("tailoredCvDraft").ValueKind);
    }

    [Fact]
    public async Task AnalyzeAll_SkipsRemainingSectionsAfterAProviderFailure()
    {
        var client = await factory.GetGuestClientAsync();
        await ProfileEntryTests.AddExperienceAsync(client, employer: $"Analyze failure {Guid.NewGuid()}", skills: []);
        var listingId = await CreateListingAsync(client, "Developer", "Build software", []);
        var calls = 0;
        factory.Llm.Respond = () =>
        {
            calls++;
            if (calls == 1) throw new HttpRequestException("provider detail", null, HttpStatusCode.ServiceUnavailable);
            var instructions = factory.Llm.LastMessages[0].Text;
            var json = instructions.Contains("existing experience bullet IDs", StringComparison.Ordinal)
                ? "{\"selectedBulletIds\":[]}"
                : instructions.Contains("profile skill IDs", StringComparison.Ordinal)
                    ? "{\"selectedSkillIds\":[]}" : "{\"summary\":null}";
            return new ChatResponse(new ChatMessage(ChatRole.Assistant, json));
        };

        var payload = (await client.GraphQLAsync(AnalyzeAll, new { listingId })).GetProperty("analyzeAllTailoredCv");
        var results = payload.GetProperty("analysis").GetProperty("results").EnumerateArray().ToList();

        Assert.Equal("FAILED", results[0].GetProperty("status").GetString());
        Assert.Contains("couldn't be reached", results[0].GetProperty("error").GetString());
        Assert.All(results.Skip(1), result => Assert.Equal("SKIPPED", result.GetProperty("status").GetString()));
        Assert.Equal(1, calls);
    }

    private static async Task AddPrivateProfileDetailsAsync(HttpClient client)
    {
        await client.GraphQLAsync("""
            mutation {
              updateProfile(input: {
                fullName: "PRIVATE_FULL_NAME", headline: "Engineer", summary: "Profile summary",
                email: "PRIVATE_EMAIL@example.test", phone: "PRIVATE_PHONE", dateOfBirth: "1990-01-01",
                nationality: "PRIVATE_NATIONALITY", workAuthorization: "PRIVATE_AUTH", hobbies: [], links: []
              }) { profile { id } }
            }
            """);
        await client.GraphQLAsync("""
            mutation {
              updateProfilePreferences(input: {
                jobRoleNames: ["PRIVATE_PREFERENCE"], workModes: [], employmentTypes: [],
                minSalary: 100, salaryCurrency: "NOK", noticePeriodMonths: 1
              }) { profile { id } }
            }
            """);
        await client.GraphQLAsync("""
            mutation {
              addProfileReference(input: { name: "PRIVATE_REFERENCE", relationship: "Manager" }) {
                profileReference { id }
              }
            }
            """);
    }

    private async Task<JsonElement> AnalyzeAsync(HttpClient client, int listingId, string section, int? sourceEntryId)
    {
        var data = await client.GraphQLAsync(Analyze, new { listingId, section, sourceEntryId });
        return data.GetProperty("analyzeTailoredCvSection");
    }

    internal static async Task<int> CreateListingAsync(HttpClient client, string title, string description, string[] skills,
        string? postingLanguage = null)
    {
        var listing = new
        {
            title, companyName = "Tailored CV analysis company", jobRoleName = "Developer",
            description, url = $"https://tailored-cv-analysis.example/{Guid.NewGuid()}", postedOn = "2026-10-01",
            deadlineType = "UNSPECIFIED", skills = skills.Select(name => (object)new { name, level = 3, isRequired = true }).ToArray(),
            languages = Array.Empty<object>(), locations = Array.Empty<object>(), postingLanguage,
        };
        var data = await client.GraphQLAsync("""
            mutation ($listing: ListingInput!) { createListing(input: { listing: $listing }) { listing { id } } }
            """, new { listing });
        return data.GetProperty("createListing").GetProperty("listing").GetProperty("id").GetInt32();
    }
}
