using System.Text.Json;
using JobHunt.Api.Data;
using JobHunt.Api.Tests.Features.Listings;
using JobHunt.Api.Tests.Features.Profiles;
using JobHunt.Api.Tests.Features.WritingSamples;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace JobHunt.Api.Tests.Features.CoverLetters;

public class CoverLetterTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Content = "language fullName date organization recipientName subject greeting paragraphs closing";
    private const string Save = $$"""
        mutation ($listingId: Int!, $content: CoverLetterContentInput!, $brief: String) {
          saveCoverLetterDraft(input: { listingId: $listingId, content: $content, brief: $brief }) {
            coverLetterDraft { brief content { {{Content}} } }
            errors { __typename }
          }
        }
        """;
    private const string Generate = $$"""
        mutation ($listingId: Int!) {
          generateCoverLetter(input: { listingId: $listingId }) {
            result { status error errorCode content { {{Content}} } }
            errors { __typename }
          }
        }
        """;
    private const string Preview = $$"""
        mutation ($listingId: Int!, $json: String!) {
          previewCoverLetterDraft(input: { listingId: $listingId, json: $json }) {
            candidate { {{Content}} }
            errors { __typename ... on Error { message } }
          }
        }
        """;
    private const string LetterJson = """{"subject":"Application: Backend developer","greeting":"Dear Kari,","paragraphs":["I build things."," ","I would love to join."],"closing":"Best regards,"}""";

    [Fact]
    public async Task Save_NormalizesAndDefaultsToPostingLanguage()
    {
        var client = await factory.GetOwnerClientAsync();
        var listingId = await CreateListingAsync(client, "NORWEGIAN");

        var payload = (await client.GraphQLAsync(Save, new
        {
            listingId, brief = "  Stress backend work  ",
            content = new { greeting = " Hei, ", paragraphs = new[] { "Første avsnitt.", "  " } },
        })).GetProperty("saveCoverLetterDraft");

        var draft = payload.GetProperty("coverLetterDraft");
        Assert.Equal("Stress backend work", draft.GetProperty("brief").GetString());
        var content = draft.GetProperty("content");
        Assert.Equal("NORWEGIAN", content.GetProperty("language").GetString());
        Assert.Equal("Hei,", content.GetProperty("greeting").GetString());
        Assert.Equal(["Første avsnitt."], content.GetProperty("paragraphs").EnumerateArray().Select(p => p.GetString()));
        var read = await client.GraphQLAsync($$"""
            query ($listingId: Int!) { coverLetterDraft(listingId: $listingId) { content { greeting } } }
            """, new { listingId });
        Assert.Equal("Hei,", read.GetProperty("coverLetterDraft").GetProperty("content").GetProperty("greeting").GetString());
    }

    [Fact]
    public async Task Save_TooManyParagraphs_IsRejected()
    {
        var client = await factory.GetOwnerClientAsync();
        var listingId = await CreateListingAsync(client);

        var payload = (await client.GraphQLAsync(Save, new
        {
            listingId, content = new { paragraphs = Enumerable.Repeat("Text.", 11) },
        })).GetProperty("saveCoverLetterDraft");

        Assert.Equal("InvalidCoverLetterError", payload.GetProperty("errors")[0].GetProperty("__typename").GetString());
    }

    [Fact]
    public async Task Save_OtherUsersListing_IsNotFound()
    {
        var owner = await factory.GetOwnerClientAsync();
        var guest = await factory.GetGuestClientAsync();
        var listingId = await CreateListingAsync(owner);

        var payload = (await guest.GraphQLAsync(Save, new { listingId, content = new { } })).GetProperty("saveCoverLetterDraft");

        Assert.Equal("ListingNotFoundError", payload.GetProperty("errors")[0].GetProperty("__typename").GetString());
        var read = await guest.GraphQLAsync("query ($listingId: Int!) { coverLetterDraft(listingId: $listingId) { id } }", new { listingId });
        Assert.Equal(JsonValueKind.Null, read.GetProperty("coverLetterDraft").ValueKind);
    }

    [Fact]
    public async Task Generate_SendsStorySamplesAndBrief_ButNoPrivateDetails()
    {
        var owner = await factory.GetOwnerClientAsync();
        var guest = await factory.GetGuestClientAsync();
        await owner.GraphQLAsync("""
            mutation {
              updateProfile(input: { fullName: "Kari Nordmann", headline: "Engineer", summary: "Profile summary",
                email: "PRIVATE_EMAIL@example.test", phone: "PRIVATE_PHONE", dateOfBirth: "1990-01-01",
                nationality: "PRIVATE_NATIONALITY", workAuthorization: "PRIVATE_AUTH", hobbies: ["Climbing"], links: [] }) { errors { __typename } }
              updateProfileStory(input: { origin: "STORY_ORIGIN", motivation: "STORY_MOTIVATION" }) { errors { __typename } }
            }
            """);
        await WritingSampleTests.AddAsync(owner, "Owner sample", "OWNER_SAMPLE_TEXT");
        await WritingSampleTests.AddAsync(guest, "Guest sample", "GUEST_SAMPLE_TEXT");
        var listingId = await CreateListingAsync(owner);
        await owner.GraphQLAsync(Save, new { listingId, content = new { }, brief = "BRIEF_TEXT" });
        var companyId = (await owner.GraphQLAsync("query ($id: Int!) { listingById(id: $id) { company { id } } }", new { id = listingId }))
            .GetProperty("listingById").GetProperty("company").GetProperty("id").GetInt32();
        await owner.GraphQLAsync("mutation ($id: Int!) { setCompanyNotes(input: { id: $id, notes: \"COMPANY_NOTES\" }) { errors { __typename } } }", new { id = companyId });
        factory.Llm.Respond = () => new ChatResponse(new ChatMessage(ChatRole.Assistant, LetterJson));

        var payload = (await owner.GraphQLAsync(Generate, new { listingId })).GetProperty("generateCoverLetter");

        var result = payload.GetProperty("result");
        Assert.Equal("SUCCEEDED", result.GetProperty("status").GetString());
        var content = result.GetProperty("content");
        Assert.Equal(["I build things.", "I would love to join."], content.GetProperty("paragraphs").EnumerateArray().Select(p => p.GetString()));
        Assert.Equal("Kari Nordmann", content.GetProperty("fullName").GetString());
        Assert.Equal("Cover letter company", content.GetProperty("organization").GetString());
        Assert.Equal("Application: Backend developer", content.GetProperty("subject").GetString());
        var prompt = string.Join("\n", factory.Llm.LastMessages.Select(m => m.Text));
        foreach (var expected in new[] { "STORY_ORIGIN", "STORY_MOTIVATION", "Climbing", "OWNER_SAMPLE_TEXT", "BRIEF_TEXT", "Build services", "COMPANY_NOTES", "https://" })
            Assert.Contains(expected, prompt);
        Assert.DoesNotContain("PRIVATE_", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("GUEST_SAMPLE_TEXT", prompt, StringComparison.Ordinal);
        // Generation never saves: the draft keeps its empty content.
        var draft = await owner.GraphQLAsync("query ($listingId: Int!) { coverLetterDraft(listingId: $listingId) { content { paragraphs } } }", new { listingId });
        Assert.Empty(draft.GetProperty("coverLetterDraft").GetProperty("content").GetProperty("paragraphs").EnumerateArray());
    }

    [Fact]
    public async Task Generate_UsesTheSavedTailoredCv()
    {
        var client = await factory.GetOwnerClientAsync();
        var listingId = await CreateListingAsync(client);
        await client.GraphQLAsync("""
            mutation ($listingId: Int!) {
              saveTailoredCvDraft(input: { listingId: $listingId, content: { summary: "CV_DRAFT_SUMMARY",
                experiences: [{ employer: "Tech Co", title: "Dev", technologies: ["CV_TECH"], bullets: [] }] } }) { errors { __typename } }
            }
            """, new { listingId });
        factory.Llm.Respond = () => new ChatResponse(new ChatMessage(ChatRole.Assistant, LetterJson));

        await client.GraphQLAsync(Generate, new { listingId });

        var prompt = string.Join("\n", factory.Llm.LastMessages.Select(m => m.Text));
        Assert.Contains("CV_DRAFT_SUMMARY", prompt);
        Assert.Contains("\"technologies\":[\"CV_TECH\"]", prompt);
    }

    [Fact]
    public async Task Generate_SendsEntryTechnologies_AndApplicationRules()
    {
        var client = await factory.GetOwnerClientAsync();
        var skill = ListingTests.Unique("Svelte");
        await ProfileEntryTests.AddExperienceAsync(client, employer: "Tech Co", skills: [skill]);
        var listingId = await CreateListingAsync(client);
        factory.Llm.Respond = () => new ChatResponse(new ChatMessage(ChatRole.Assistant, LetterJson));

        await client.GraphQLAsync(Generate, new { listingId });

        var instructions = factory.Llm.LastMessages[0].Text;
        Assert.Contains("writing in a language is not evidence of speaking it", instructions);
        Assert.Contains("A BROKER listing is usually a client", instructions);
        Assert.Contains("never relabel them as fullstack", instructions);
        Assert.Contains("reference number from the description's Application section", instructions);
        Assert.Contains($"\"technologies\":[\"{skill}\"]", factory.Llm.LastMessages[1].Text);
    }

    [Fact]
    public async Task Generate_UnreadableAnswer_FailsAndLogsInvalidJson()
    {
        var client = await factory.GetOwnerClientAsync();
        var listingId = await CreateListingAsync(client);
        factory.Llm.Respond = () => new ChatResponse(new ChatMessage(ChatRole.Assistant, """{"letter":"nope"}"""));

        var result = (await client.GraphQLAsync(Generate, new { listingId })).GetProperty("generateCoverLetter").GetProperty("result");

        Assert.Equal("FAILED", result.GetProperty("status").GetString());
        Assert.Equal("invalid-json", result.GetProperty("errorCode").GetString());
        using var scope = factory.Services.CreateScope();
        var usage = await scope.ServiceProvider.GetRequiredService<AppDbContext>().LlmUsages
            .IgnoreQueryFilters().OrderByDescending(u => u.Id).FirstAsync();
        Assert.Equal("cover-letter", usage.Operation);
        Assert.False(usage.Success);
        Assert.Equal("invalid-json", usage.ErrorCode);
    }

    [Fact]
    public async Task ExternalPrompt_AndPreview()
    {
        var client = await factory.GetOwnerClientAsync();
        var listingId = await CreateListingAsync(client);

        var prompt = (await client.GraphQLAsync("""
            query ($listingId: Int!) { coverLetterExternalPrompt(listingId: $listingId) { version instructions } }
            """, new { listingId })).GetProperty("coverLetterExternalPrompt");
        Assert.Equal(5, prompt.GetProperty("version").GetInt32());
        Assert.Contains("Build services", prompt.GetProperty("instructions").GetString());
        Assert.Contains("Never search the web for the posting", prompt.GetProperty("instructions").GetString());
        Assert.Contains("\"schemaVersion\": 5", prompt.GetProperty("instructions").GetString());

        var valid = (await client.GraphQLAsync(Preview, new { listingId, json = LetterJson.Replace("{\"subject\"", "{\"schemaVersion\":5,\"subject\"") }))
            .GetProperty("previewCoverLetterDraft");
        Assert.Equal("Dear Kari,", valid.GetProperty("candidate").GetProperty("greeting").GetString());

        foreach (var json in new[] { LetterJson, """{"schemaVersion":3,"subject":null,"greeting":"Hi","paragraphs":["x"],"closing":"Bye","extra":1}""", "not json" })
        {
            var invalid = (await client.GraphQLAsync(Preview, new { listingId, json })).GetProperty("previewCoverLetterDraft");
            Assert.Equal("InvalidCoverLetterError", invalid.GetProperty("errors")[0].GetProperty("__typename").GetString());
        }
    }

    [Fact]
    public async Task Prefill_BuildsNorwegianHeaderWithoutGreeting()
    {
        var client = await factory.GetOwnerClientAsync();
        var listingId = await CreateListingAsync(client, "NORWEGIAN");

        var content = (await client.GraphQLAsync($$"""
            query ($listingId: Int!) { coverLetterDraftFromProfile(listingId: $listingId) { {{Content}} } }
            """, new { listingId })).GetProperty("coverLetterDraftFromProfile");

        Assert.Equal("Søknad på stilling som Backend developer", content.GetProperty("subject").GetString());
        Assert.Equal(JsonValueKind.Null, content.GetProperty("greeting").ValueKind);
        Assert.Equal("Vennlig hilsen", content.GetProperty("closing").GetString());
        Assert.Equal("Cover letter company", content.GetProperty("organization").GetString());
        Assert.Equal(DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd"), content.GetProperty("date").GetString());
        Assert.Empty(content.GetProperty("paragraphs").EnumerateArray());
        var english = (await client.GraphQLAsync("""
            query ($listingId: Int!) { coverLetterDraftFromProfile(listingId: $listingId) { greeting } }
            """, new { listingId = await CreateListingAsync(client) })).GetProperty("coverLetterDraftFromProfile");
        Assert.Equal("Dear hiring team,", english.GetProperty("greeting").GetString());
    }

    [Fact]
    public async Task ProfileAddress_FillsTheSenderBlock_ButNeverReachesTheAi()
    {
        var client = await factory.GetOwnerClientAsync();
        await client.GraphQLAsync("""
            mutation {
              updateProfile(input: { fullName: "Kari Nordmann", location: { city: "Molde", countryCode: "NO" },
                                     hobbies: [], links: [] }) { errors { __typename } }
            }
            """);
        var updated = (await client.GraphQLAsync("""
            mutation { updateProfileAddress(input: { streetAddress: " Storgata 1 ", postalCode: "6413" }) {
              profile { streetAddress postalCode } errors { __typename } } }
            """)).GetProperty("updateProfileAddress").GetProperty("profile");
        Assert.Equal("Storgata 1", updated.GetProperty("streetAddress").GetString());
        var listingId = await CreateListingAsync(client, "NORWEGIAN");

        var header = (await client.GraphQLAsync("""
            query ($listingId: Int!) { coverLetterDraftFromProfile(listingId: $listingId) { address } }
            """, new { listingId })).GetProperty("coverLetterDraftFromProfile");
        factory.Llm.Respond = () => new ChatResponse(new ChatMessage(ChatRole.Assistant, LetterJson));
        await client.GraphQLAsync(Generate, new { listingId });

        Assert.Equal("Storgata 1\n6413 Molde", header.GetProperty("address").GetString());
        var prompt = string.Join("\n", factory.Llm.LastMessages.Select(m => m.Text));
        Assert.DoesNotContain("Storgata", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("6413", prompt, StringComparison.Ordinal);
        await client.GraphQLAsync("mutation { updateProfileAddress(input: { streetAddress: null, postalCode: null }) { errors { __typename } } }");
    }

    [Fact]
    public async Task PasteText_SplitsTheLetterWithoutAi()
    {
        var client = await factory.GetOwnerClientAsync();
        var listingId = await CreateListingAsync(client, "NORWEGIAN");
        var calls = factory.Llm.Calls;

        var payload = (await client.GraphQLAsync($$"""
            mutation ($listingId: Int!, $text: String!) {
              previewCoverLetterText(input: { listingId: $listingId, text: $text }) {
                candidate { {{Content}} }
                errors { __typename }
              }
            }
            """, new { listingId, text = "Søknad på stilling som utvikler\n\nFørste avsnitt.\n\nAndre avsnitt.\n\nVennlig hilsen\nKari" }))
            .GetProperty("previewCoverLetterText");

        var candidate = payload.GetProperty("candidate");
        Assert.Equal("Søknad på stilling som utvikler", candidate.GetProperty("subject").GetString());
        Assert.Equal(["Første avsnitt.", "Andre avsnitt."], candidate.GetProperty("paragraphs").EnumerateArray().Select(p => p.GetString()));
        Assert.Equal("Cover letter company", candidate.GetProperty("organization").GetString());
        Assert.Equal(calls, factory.Llm.Calls);
    }

    [Fact]
    public async Task GenerateDocument_StoresUnsentCoverLetterPdf()
    {
        var client = await factory.GetOwnerClientAsync();
        var listingId = await CreateListingAsync(client);
        const string GenerateDocument = """
            mutation ($listingId: Int!) {
              generateCoverLetterDocument(input: { listingId: $listingId }) {
                document { id kind fileName sentAt }
                errors { __typename }
              }
            }
            """;
        var missing = (await client.GraphQLAsync(GenerateDocument, new { listingId })).GetProperty("generateCoverLetterDocument");
        Assert.Equal("CoverLetterDraftNotFoundError", missing.GetProperty("errors")[0].GetProperty("__typename").GetString());
        await client.GraphQLAsync(Save, new { listingId, content = new { subject = "Application", paragraphs = new[] { "PDF_BODY_TEXT." } } });

        var document = (await client.GraphQLAsync(GenerateDocument, new { listingId }))
            .GetProperty("generateCoverLetterDocument").GetProperty("document");

        Assert.Equal("COVER_LETTER", document.GetProperty("kind").GetString());
        Assert.Contains("_CoverLetter_", document.GetProperty("fileName").GetString());
        Assert.Equal(JsonValueKind.Null, document.GetProperty("sentAt").ValueKind);
        var bytes = await client.GetByteArrayAsync($"/api/documents/{document.GetProperty("id").GetInt32()}");
        using var pdf = UglyToad.PdfPig.PdfDocument.Open(bytes);
        Assert.Contains("PDF_BODY_TEXT.", string.Join("\n", pdf.GetPages().Select(p => p.Text)));
    }

    private static async Task<int> CreateListingAsync(HttpClient client, string? postingLanguage = null)
    {
        var listing = new
        {
            title = "Backend developer", companyName = "Cover letter company", jobRoleName = "Developer",
            description = "Build services", url = $"https://cover-letter.example/{Guid.NewGuid()}", postedOn = "2026-10-01",
            deadlineType = "UNSPECIFIED", skills = Array.Empty<object>(), languages = Array.Empty<object>(),
            locations = Array.Empty<object>(), postingLanguage,
        };
        var data = await client.GraphQLAsync("""
            mutation ($listing: ListingInput!) { createListing(input: { listing: $listing }) { listing { id } } }
            """, new { listing });
        return data.GetProperty("createListing").GetProperty("listing").GetProperty("id").GetInt32();
    }
}
