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
            result { status section sourceEntryId selectedBulletIds selectedSkillIds summary error }
            errors { __typename ... on Error { message } }
          }
        }
        """;
    private const string AnalyzeAll = """
        mutation ($listingId: Int!) {
          analyzeAllTailoredCv(input: { listingId: $listingId }) {
            analysis { results { status section sourceEntryId selectedBulletIds selectedSkillIds summary error } }
            errors { __typename ... on Error { message } }
          }
        }
        """;
    private const string PreviewExternal = """
        mutation ($listingId: Int!, $json: String!) {
          previewTailoredCvDraft(input: { listingId: $listingId, json: $json }) {
            candidate { schemaVersion content { summary experiences { sourceEntryId employer title bullets { sourceBulletId text } } skills { sourceSkillId name level } } }
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
            $$"""{"selectedBulletIds":[{{bulletId}}]}"""));

        var payload = await AnalyzeAsync(client, listingId, "EXPERIENCE", entryId);

        var result = payload.GetProperty("result");
        Assert.Equal("SUCCEEDED", result.GetProperty("status").GetString());
        Assert.Equal(new[] { bulletId }, result.GetProperty("selectedBulletIds").EnumerateArray().Select(v => v.GetInt32()));
        var prompt = string.Join("\n", factory.Llm.LastMessages.Select(m => m.Text));
        Assert.Contains("Allowed Employer", prompt);
        Assert.Contains("Build React services", prompt);
        Assert.DoesNotContain("PRIVATE_", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("Allowed Employer", prompt.Split("<cv_context>")[0], StringComparison.Ordinal);
        Assert.Equal(ChatResponseFormat.Json, factory.Llm.LastOptions!.ResponseFormat);
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
        var listingId = await CreateListingAsync(client, "Vue developer", "Build Vue applications", ["Vue"]);
        factory.Llm.Respond = () => new ChatResponse(new ChatMessage(ChatRole.Assistant,
            "{\"summary\":\"Frontend developer focused on Vue applications.\"}"));

        var result = (await AnalyzeAsync(client, listingId, "SUMMARY", null)).GetProperty("result");

        Assert.Equal("SUCCEEDED", result.GetProperty("status").GetString());
        Assert.Equal("Frontend developer focused on Vue applications.", result.GetProperty("summary").GetString());
        Assert.Contains("Builds Vue applications", string.Join("\n", factory.Llm.LastMessages.Select(m => m.Text)));
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
        Assert.Contains("sourceBulletId", instructions);
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
        var listingId = await CreateListingAsync(client, "Developer", new string('x', TailoredCvAnalyzer.MaxContextLength + 1), []);
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
    public async Task AnalyzeAll_RunsOneCallPerExperienceThenSkillsAndSummary_AndLogsEach()
    {
        var client = await factory.GetOwnerClientAsync();
        await ProfileEntryTests.AddExperienceAsync(client, employer: $"Analyze all {Guid.NewGuid()}", skills: []);
        var profile = await client.GraphQLAsync("{ profile { experiences { id } } }");
        var experienceCount = profile.GetProperty("profile").GetProperty("experiences").GetArrayLength();
        var listingId = await CreateListingAsync(client, "Full stack developer", "Build web applications", []);
        factory.Llm.Respond = () =>
        {
            var instructions = factory.Llm.LastMessages[0].Text;
            var json = instructions.Contains("existing experience bullet IDs", StringComparison.Ordinal)
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
        Assert.Equal(experienceCount + 2, results.Count);
        Assert.Equal(experienceCount, results.Count(r => r.GetProperty("section").GetString() == "EXPERIENCE"));
        Assert.Equal("SKILLS", results[^2].GetProperty("section").GetString());
        Assert.Equal("SUMMARY", results[^1].GetProperty("section").GetString());
        Assert.All(results, result => Assert.Equal("SUCCEEDED", result.GetProperty("status").GetString()));
        Assert.Equal(usageBefore + experienceCount + 2, await db.LlmUsages.IgnoreQueryFilters()
            .CountAsync(u => u.Operation == TailoredCvAnalyzer.Operation));
        Assert.Equal(JsonValueKind.Null, (await client.GraphQLAsync("""
            query ($listingId: Int!) { tailoredCvDraft(listingId: $listingId) { id } }
            """, new { listingId })).GetProperty("tailoredCvDraft").ValueKind);
    }

    [Fact]
    public async Task AnalyzeAll_ContinuesAfterAProviderFailure()
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
        Assert.All(results.Skip(1), result => Assert.Equal("SUCCEEDED", result.GetProperty("status").GetString()));
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

    private static async Task<int> CreateListingAsync(HttpClient client, string title, string description, string[] skills)
    {
        var listing = new
        {
            title, companyName = "Tailored CV analysis company", jobRoleName = "Developer",
            description, url = $"https://tailored-cv-analysis.example/{Guid.NewGuid()}", postedOn = "2026-10-01",
            deadlineType = "UNSPECIFIED", skills = skills.Select(name => (object)new { name, level = 3, isRequired = true }).ToArray(),
            languages = Array.Empty<object>(), locations = Array.Empty<object>(),
        };
        var data = await client.GraphQLAsync("""
            mutation ($listing: ListingInput!) { createListing(input: { listing: $listing }) { listing { id } } }
            """, new { listing });
        return data.GetProperty("createListing").GetProperty("listing").GetProperty("id").GetInt32();
    }
}
