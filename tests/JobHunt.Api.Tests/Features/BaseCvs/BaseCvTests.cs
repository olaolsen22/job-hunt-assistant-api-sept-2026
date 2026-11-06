using System.Net;
using System.Text.Json;
using JobHunt.Api.Tests.Features.Documents;
using JobHunt.Api.Tests.Features.Listings;
using JobHunt.Api.Tests.Features.Profiles;

namespace JobHunt.Api.Tests.Features.BaseCvs;

public class BaseCvTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string BaseCvFields =
        "id name description languages archivedAt latestVersion { id number note language content { summary language } } versions { id number }";
    private const string Errors = "errors { __typename ... on Error { message } ... on DuplicateBaseCvError { existingBaseCvId } }";
    private const string Create = $$"""
        mutation ($name: String!, $language: Language!, $content: TailoredCvContentInput!, $description: String, $note: String) {
          createBaseCv(input: { name: $name, language: $language, content: $content, description: $description, note: $note }) {
            baseCv { {{BaseCvFields}} }
            {{Errors}}
          }
        }
        """;
    private const string Save = $$"""
        mutation ($baseCvId: Int!, $language: Language!, $content: TailoredCvContentInput!, $note: String) {
          saveBaseCvVersion(input: { baseCvId: $baseCvId, language: $language, content: $content, note: $note }) {
            baseCvVersion { id number note language content { summary language skills { sourceSkillId categoryId } } baseCv { name } }
            errors { __typename ... on Error { message } }
          }
        }
        """;
    private const string Generate = """
        mutation ($listingId: Int!, $baseCvVersionId: Int!) {
          generateBaseCvDocument(input: { listingId: $listingId, baseCvVersionId: $baseCvVersionId }) {
            document { id kind origin sentAt baseCvVersion { number baseCv { name } } }
            errors { __typename }
          }
        }
        """;
    private const string Delete = """
        mutation ($id: Int!) { deleteBaseCv(input: { id: $id }) { id errors { __typename } } }
        """;
    private const string List = $$"""
        query ($archived: Boolean!) { baseCvs(archived: $archived) { {{BaseCvFields}} } }
        """;

    [Fact]
    public async Task Create_SavesVersionOne_InTheBaseCvsLanguage()
    {
        var client = await factory.GetOwnerClientAsync();

        var payload = await CreateAsync(client, ListingTests.Unique("Frontend"), "NORWEGIAN",
            new { summary = " Bygger brukergrensesnitt. ", language = "ENGLISH" }, note: " First ");

        var baseCv = payload.GetProperty("baseCv");
        Assert.Equal(["NORWEGIAN"], baseCv.GetProperty("languages").EnumerateArray().Select(l => l.GetString()));
        var version = baseCv.GetProperty("latestVersion");
        Assert.Equal("NORWEGIAN", version.GetProperty("language").GetString());
        Assert.Equal(1, version.GetProperty("number").GetInt32());
        Assert.Equal("First", version.GetProperty("note").GetString());
        Assert.Equal("Bygger brukergrensesnitt.", version.GetProperty("content").GetProperty("summary").GetString());
        Assert.Equal("NORWEGIAN", version.GetProperty("content").GetProperty("language").GetString());
    }

    [Fact]
    public async Task SaveVersion_AddsOneOnlyWhenTheContentChanges()
    {
        var client = await factory.GetOwnerClientAsync();
        var baseCv = (await CreateAsync(client, ListingTests.Unique("Backend"), content: new { summary = "Builds APIs." }))
            .GetProperty("baseCv");
        var id = baseCv.GetProperty("id").GetInt32();
        var first = baseCv.GetProperty("latestVersion").GetProperty("id").GetInt32();

        var same = (await SaveAsync(client, id, new { summary = " Builds APIs. " }, "ignored")).GetProperty("baseCvVersion");
        var changed = (await SaveAsync(client, id, new { summary = "Builds and runs APIs." }, "Run duty")).GetProperty("baseCvVersion");

        Assert.Equal(first, same.GetProperty("id").GetInt32());
        Assert.Equal(1, same.GetProperty("number").GetInt32());
        Assert.Equal(JsonValueKind.Null, same.GetProperty("note").ValueKind);
        Assert.Equal(2, changed.GetProperty("number").GetInt32());
        Assert.Equal("Run duty", changed.GetProperty("note").GetString());
        Assert.Equal(baseCv.GetProperty("name").GetString(), changed.GetProperty("baseCv").GetProperty("name").GetString());
        var listed = (await ListAsync(client)).Single(b => b.GetProperty("id").GetInt32() == id);
        Assert.Equal([2, 1], listed.GetProperty("versions").EnumerateArray().Select(v => v.GetProperty("number").GetInt32()));
        Assert.Equal(2, listed.GetProperty("latestVersion").GetProperty("number").GetInt32());
    }

    [Fact]
    public async Task SaveVersion_InAnotherLanguage_NumbersEachLanguageSeparately()
    {
        var client = await factory.GetOwnerClientAsync();
        var id = (await CreateAsync(client, ListingTests.Unique("Bilingual"), content: new { summary = "Builds UIs." }))
            .GetProperty("baseCv").GetProperty("id").GetInt32();

        var norwegian = (await SaveAsync(client, id, new { summary = "Bygger grensesnitt.", language = "ENGLISH" },
            language: "NORWEGIAN")).GetProperty("baseCvVersion");
        var english = (await SaveAsync(client, id, new { summary = "Builds and tests UIs." })).GetProperty("baseCvVersion");
        var listed = (await client.GraphQLAsync("""
            query {
              baseCvs {
                id languages latestVersion { number language }
                norwegian: latestVersion(language: NORWEGIAN) { number }
                english: versions(language: ENGLISH) { number }
              }
            }
            """)).GetProperty("baseCvs").EnumerateArray().Single(b => b.GetProperty("id").GetInt32() == id);

        Assert.Equal(1, norwegian.GetProperty("number").GetInt32());
        Assert.Equal("NORWEGIAN", norwegian.GetProperty("content").GetProperty("language").GetString());
        Assert.Equal(2, english.GetProperty("number").GetInt32());
        Assert.Equal(["ENGLISH", "NORWEGIAN"], listed.GetProperty("languages").EnumerateArray().Select(l => l.GetString()));
        Assert.Equal("ENGLISH", listed.GetProperty("latestVersion").GetProperty("language").GetString());
        Assert.Equal(1, listed.GetProperty("norwegian").GetProperty("number").GetInt32());
        Assert.Equal([2, 1], listed.GetProperty("english").EnumerateArray().Select(v => v.GetProperty("number").GetInt32()));
    }

    [Fact]
    public async Task Translation_SwapsProfileTextThroughSourceIds_AndReportsTheRest()
    {
        var client = await factory.GetOwnerClientAsync();
        var employer = ListingTests.Unique("Translated employer");
        var experienceId = (await ProfileEntryTests.AddExperienceAsync(client, employer: employer)).GetProperty("id").GetInt32();
        var translated = await AddBulletAsync(client, experienceId, "Built the checkout");
        var untranslated = await AddBulletAsync(client, experienceId, "Ran the design system");
        await client.GraphQLAsync("""
            mutation ($experienceId: Int!, $bulletId: Int!) {
              setProfileExperienceTranslation(input: { experienceId: $experienceId, language: NORWEGIAN, title: "Frontendutvikler" }) { errors { __typename } }
              setProfileBulletTranslation(input: { bulletId: $bulletId, language: NORWEGIAN, text: "Bygde kassen" }) { errors { __typename } }
            }
            """, new { experienceId, bulletId = translated });
        var id = (await CreateAsync(client, ListingTests.Unique("Translate"), content: new
        {
            summary = "My own summary.",
            experiences = new[]
            {
                new
                {
                    employer, title = "Frontend Developer", sourceEntryId = experienceId, paragraph = "Led the UI.",
                    bullets = new object[]
                    {
                        new { text = "Built the checkout", sourceBulletId = translated },
                        new { text = "Ran the design system", sourceBulletId = untranslated },
                        new { text = "Mentored juniors" },
                    },
                },
            },
        })).GetProperty("baseCv").GetProperty("id").GetInt32();

        var result = await TranslationAsync(client, id, "NORWEGIAN");

        Assert.Equal("ENGLISH", result.GetProperty("fromLanguage").GetString());
        var content = result.GetProperty("content");
        Assert.Equal("NORWEGIAN", content.GetProperty("language").GetString());
        Assert.Equal("My own summary.", content.GetProperty("summary").GetString());
        var experience = content.GetProperty("experiences")[0];
        Assert.Equal("Frontendutvikler", experience.GetProperty("title").GetString());
        Assert.Equal(["Bygde kassen", "Ran the design system", "Mentored juniors"],
            experience.GetProperty("bullets").EnumerateArray().Select(b => b.GetProperty("text").GetString()));
        Assert.Equal(translated, experience.GetProperty("bullets")[0].GetProperty("sourceBulletId").GetInt32());
        Assert.Equal(["summary", "experiences[0].bullets[1]", "experiences[0].bullets[2]", "experiences[0].paragraph"],
            result.GetProperty("untranslatedFields").EnumerateArray().Select(f => f.GetString()));
    }

    [Fact]
    public async Task Translation_IsNullWithoutASourceVersion_OrForAnotherUser()
    {
        var owner = await factory.GetOwnerClientAsync();
        var guest = await factory.GetGuestClientAsync();
        var id = (await CreateAsync(owner, ListingTests.Unique("Single"), "NORWEGIAN")).GetProperty("baseCv")
            .GetProperty("id").GetInt32();

        Assert.Equal(JsonValueKind.Null, (await TranslationAsync(owner, id, "NORWEGIAN")).ValueKind);
        Assert.Equal(JsonValueKind.Null, (await TranslationAsync(owner, id, "NORWEGIAN", fromLanguage: "ENGLISH")).ValueKind);
        Assert.NotEqual(JsonValueKind.Null, (await TranslationAsync(owner, id, "ENGLISH")).ValueKind);
        Assert.Equal(JsonValueKind.Null, (await TranslationAsync(guest, id, "ENGLISH")).ValueKind);
    }

    [Fact]
    public async Task Create_RejectsDuplicateNamesBlankNamesAndUnknownSources()
    {
        var client = await factory.GetOwnerClientAsync();
        var name = ListingTests.Unique("Fullstack");
        var existing = (await CreateAsync(client, name)).GetProperty("baseCv").GetProperty("id").GetInt32();

        var duplicate = (await CreateAsync(client, $"  {name.ToUpperInvariant()} ")).GetProperty("errors")[0];
        var blank = (await CreateAsync(client, "   ")).GetProperty("errors")[0];
        var unknownSource = (await CreateAsync(client, ListingTests.Unique("Generic"), content: new
        {
            experiences = new[] { new { employer = "Acme", title = "Dev", sourceEntryId = int.MaxValue, bullets = Array.Empty<object>() } },
        })).GetProperty("errors")[0];

        Assert.Equal("DuplicateBaseCvError", duplicate.GetProperty("__typename").GetString());
        Assert.Equal(existing, duplicate.GetProperty("existingBaseCvId").GetInt32());
        Assert.Equal("InvalidBaseCvError", blank.GetProperty("__typename").GetString());
        Assert.Equal("InvalidTailoredCvDraftError", unknownSource.GetProperty("__typename").GetString());
    }

    [Fact]
    public async Task Archive_HidesFromTheDefaultList_AndUpdateRenames()
    {
        var client = await factory.GetOwnerClientAsync();
        var id = (await CreateAsync(client, ListingTests.Unique("Old"))).GetProperty("baseCv").GetProperty("id").GetInt32();
        var newName = ListingTests.Unique("Renamed");

        var renamed = (await client.GraphQLAsync($$"""
            mutation ($id: Int!, $name: String!) {
              updateBaseCv(input: { id: $id, name: $name, description: " For product companies " }) { baseCv { {{BaseCvFields}} } }
            }
            """, new { id, name = newName })).GetProperty("updateBaseCv").GetProperty("baseCv");
        await client.GraphQLAsync("""
            mutation ($id: Int!) { setBaseCvArchived(input: { id: $id, isArchived: true }) { baseCv { archivedAt } } }
            """, new { id });

        Assert.Equal(newName, renamed.GetProperty("name").GetString());
        Assert.Equal("For product companies", renamed.GetProperty("description").GetString());
        Assert.Equal(1, renamed.GetProperty("latestVersion").GetProperty("number").GetInt32());
        Assert.DoesNotContain(await ListAsync(client), b => b.GetProperty("id").GetInt32() == id);
        Assert.Contains(await ListAsync(client, archived: true), b => b.GetProperty("id").GetInt32() == id);
    }

    [Fact]
    public async Task GeneratedDocument_RecordsTheVersion_AndKeepsTheBaseCvFromBeingDeleted()
    {
        var client = await factory.GetOwnerClientAsync();
        var name = ListingTests.Unique("Frontend");
        var baseCv = (await CreateAsync(client, name)).GetProperty("baseCv");
        var versionId = baseCv.GetProperty("latestVersion").GetProperty("id").GetInt32();
        var listingId = await DocumentTests.CreateListingAsync(client, "Base CV company");

        var document = (await client.GraphQLAsync(Generate, new { listingId, baseCvVersionId = versionId }))
            .GetProperty("generateBaseCvDocument").GetProperty("document");
        await client.GraphQLAsync("""
            mutation ($listingId: Int!, $cvDocumentId: Int) {
              applyToListing(input: { listingId: $listingId, cvDocumentId: $cvDocumentId }) { listing { id } }
            }
            """, new { listingId, cvDocumentId = document.GetProperty("id").GetInt32() });
        var sent = Assert.Single((await DocumentsAsync(client, listingId)).EnumerateArray());
        var delete = (await client.GraphQLAsync(Delete, new { id = baseCv.GetProperty("id").GetInt32() })).GetProperty("deleteBaseCv");

        Assert.Equal("CV", document.GetProperty("kind").GetString());
        Assert.Equal("BASE_CV", document.GetProperty("origin").GetString());
        Assert.Equal(name, document.GetProperty("baseCvVersion").GetProperty("baseCv").GetProperty("name").GetString());
        Assert.NotEqual(JsonValueKind.Null, sent.GetProperty("sentAt").ValueKind);
        Assert.Equal(1, sent.GetProperty("baseCvVersion").GetProperty("number").GetInt32());
        Assert.Equal("BaseCvInUseError", delete.GetProperty("errors")[0].GetProperty("__typename").GetString());
    }

    [Fact]
    public async Task Delete_UnusedBaseCv_RemovesItsVersions()
    {
        var client = await factory.GetOwnerClientAsync();
        var baseCv = (await CreateAsync(client, ListingTests.Unique("Unused"))).GetProperty("baseCv");
        var id = baseCv.GetProperty("id").GetInt32();
        var versionId = baseCv.GetProperty("latestVersion").GetProperty("id").GetInt32();

        var deleted = (await client.GraphQLAsync(Delete, new { id })).GetProperty("deleteBaseCv");

        Assert.Equal(id, deleted.GetProperty("id").GetInt32());
        Assert.Equal(JsonValueKind.Null, (await VersionAsync(client, versionId)).ValueKind);
    }

    [Fact]
    public async Task Documents_RecordWhereTheyCameFrom()
    {
        var client = await factory.GetOwnerClientAsync();
        var listingId = await DocumentTests.CreateListingAsync(client, "Origin company");
        await DocumentTests.UploadIdAsync(client, listingId);
        await client.GraphQLAsync("""
            mutation ($listingId: Int!) {
              saveTailoredCvDraft(input: { listingId: $listingId, content: { summary: "Tailored." } }) { errors { __typename } }
              saveCoverLetterDraft(input: { listingId: $listingId, content: { paragraphs: ["Letter."] } }) { errors { __typename } }
            }
            """, new { listingId });
        await client.GraphQLAsync("""
            mutation ($listingId: Int!) {
              generateCvDocument(input: { listingId: $listingId }) { document { id } }
              generateCoverLetterDocument(input: { listingId: $listingId }) { document { id } }
            }
            """, new { listingId });

        var origins = (await DocumentsAsync(client, listingId)).EnumerateArray()
            .Select(d => $"{d.GetProperty("kind").GetString()}:{d.GetProperty("origin").GetString()}").Order();

        Assert.Equal(["COVER_LETTER:TAILORED", "CV:TAILORED", "CV:UPLOADED"], origins);
    }

    [Fact]
    public async Task SkillAndCategoryChanges_MoveVersionLinks_SoTheNextVersionSaves()
    {
        var client = await factory.GetOwnerClientAsync();
        var from = await SetProfileSkillAsync(client, ListingTests.Unique("MergeFrom"));
        var into = await SetProfileSkillAsync(client, ListingTests.Unique("MergeInto"));
        var category = (await client.GraphQLAsync("""
            mutation ($name: String!) { createSkillCategory(input: { name: $name }) { skillCategory { id } } }
            """, new { name = ListingTests.Unique("Category") })).GetProperty("createSkillCategory")
            .GetProperty("skillCategory").GetProperty("id").GetInt32();
        var baseCv = (await CreateAsync(client, ListingTests.Unique("Linked"), content: new
        {
            skills = new[] { new { name = "Old name", level = 3, sourceSkillId = (int?)from.Id, categoryId = (int?)category } },
        })).GetProperty("baseCv");
        var id = baseCv.GetProperty("id").GetInt32();
        var versionId = baseCv.GetProperty("latestVersion").GetProperty("id").GetInt32();

        await client.GraphQLAsync("""
            mutation ($operations: [SkillCleanupOperationInput!]!) { cleanUpSkills(input: { operations: $operations }) { errors { __typename } } }
            """, new { operations = new[] { new { skillIds = new[] { from.Id, into.Id }, names = new[] { into.Name } } } });
        await client.GraphQLAsync("mutation ($id: Int!) { deleteSkillCategory(input: { id: $id }) { id } }", new { id = category });

        var skill = (await VersionAsync(client, versionId)).GetProperty("content").GetProperty("skills")[0];
        Assert.Equal(into.Id, skill.GetProperty("sourceSkillId").GetInt32());
        Assert.Equal(JsonValueKind.Null, skill.GetProperty("categoryId").ValueKind);
        var next = (await SaveAsync(client, id, new
        {
            summary = "Next.",
            skills = new[] { new { name = "Old name", level = 3, sourceSkillId = (int?)into.Id } },
        })).GetProperty("baseCvVersion");
        Assert.Equal(2, next.GetProperty("number").GetInt32());
    }

    [Fact]
    public async Task OtherUsers_CantSeeOrUseABaseCv()
    {
        var owner = await factory.GetOwnerClientAsync();
        var guest = await factory.GetGuestClientAsync();
        var baseCv = (await CreateAsync(owner, ListingTests.Unique("Private"))).GetProperty("baseCv");
        var id = baseCv.GetProperty("id").GetInt32();
        var versionId = baseCv.GetProperty("latestVersion").GetProperty("id").GetInt32();
        var guestListing = await DocumentTests.CreateListingAsync(guest, "Guest company");

        Assert.DoesNotContain(await ListAsync(guest), b => b.GetProperty("id").GetInt32() == id);
        Assert.Equal(JsonValueKind.Null, (await VersionAsync(guest, versionId)).ValueKind);
        Assert.Equal("BaseCvNotFoundError", (await SaveAsync(guest, id, new { summary = "Mine now" }))
            .GetProperty("errors")[0].GetProperty("__typename").GetString());
        var other = await guest.GraphQLAsync("""
            mutation ($id: Int!) {
              updateBaseCv(input: { id: $id, name: "Hijack" }) { errors { __typename } }
              setBaseCvArchived(input: { id: $id, isArchived: true }) { errors { __typename } }
              deleteBaseCv(input: { id: $id }) { errors { __typename } }
            }
            """, new { id });
        foreach (var field in new[] { "updateBaseCv", "setBaseCvArchived", "deleteBaseCv" })
            Assert.Equal("BaseCvNotFoundError", other.GetProperty(field).GetProperty("errors")[0].GetProperty("__typename").GetString());
        var generate = (await guest.GraphQLAsync(Generate, new { listingId = guestListing, baseCvVersionId = versionId }))
            .GetProperty("generateBaseCvDocument");
        Assert.Equal("BaseCvVersionNotFoundError", generate.GetProperty("errors")[0].GetProperty("__typename").GetString());
        var ontoGuestListing = (await owner.GraphQLAsync(Generate, new { listingId = guestListing, baseCvVersionId = versionId }))
            .GetProperty("generateBaseCvDocument");
        Assert.Equal("ListingNotFoundError", ontoGuestListing.GetProperty("errors")[0].GetProperty("__typename").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await guest.GetAsync($"/api/base-cv-versions/{versionId}/pdf")).StatusCode);
    }

    [Fact]
    public async Task Preview_RendersTheVersionAsPdf()
    {
        var client = await factory.GetOwnerClientAsync();
        var versionId = (await CreateAsync(client, ListingTests.Unique("Preview"), content: new { fullName = "Kari Nordmann" }))
            .GetProperty("baseCv").GetProperty("latestVersion").GetProperty("id").GetInt32();

        var response = await client.GetAsync($"/api/base-cv-versions/{versionId}/pdf");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        Assert.StartsWith("%PDF-", System.Text.Encoding.ASCII.GetString((await response.Content.ReadAsByteArrayAsync())[..5]));
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync($"/api/base-cv-versions/{versionId}/pdf")).StatusCode);
    }

    [Fact]
    public async Task FromProfile_ReturnsTheProfileInTheRequestedLanguage()
    {
        var client = await factory.GetOwnerClientAsync();
        var employer = ListingTests.Unique("Prefill employer");
        var entryId = (await ProfileEntryTests.AddExperienceAsync(client, employer: employer)).GetProperty("id").GetInt32();

        var content = (await client.GraphQLAsync("""
            query { baseCvFromProfile(language: NORWEGIAN) { language experiences { sourceEntryId employer } } }
            """)).GetProperty("baseCvFromProfile");

        Assert.Equal("NORWEGIAN", content.GetProperty("language").GetString());
        Assert.Contains(content.GetProperty("experiences").EnumerateArray(),
            e => e.GetProperty("sourceEntryId").GetInt32() == entryId && e.GetProperty("employer").GetString() == employer);
    }

    private static async Task<JsonElement> CreateAsync(HttpClient client, string name, string language = "ENGLISH",
        object? content = null, string? note = null) =>
        (await client.GraphQLAsync(Create, new { name, language, content = content ?? new { summary = "Summary." }, note }))
            .GetProperty("createBaseCv");

    private static async Task<JsonElement> SaveAsync(HttpClient client, int baseCvId, object content, string? note = null,
        string language = "ENGLISH") =>
        (await client.GraphQLAsync(Save, new { baseCvId, language, content, note })).GetProperty("saveBaseCvVersion");

    private static async Task<JsonElement> TranslationAsync(HttpClient client, int baseCvId, string language,
        string? fromLanguage = null) =>
        (await client.GraphQLAsync("""
            query ($baseCvId: Int!, $language: Language!, $fromLanguage: Language) {
              baseCvTranslation(baseCvId: $baseCvId, language: $language, fromLanguage: $fromLanguage) {
                fromLanguage untranslatedFields
                content { language summary experiences { title bullets { text sourceBulletId } } }
              }
            }
            """, new { baseCvId, language, fromLanguage })).GetProperty("baseCvTranslation");

    private static async Task<int> AddBulletAsync(HttpClient client, int entryId, string text) =>
        (await client.GraphQLAsync("""
            mutation ($entryId: Int!, $text: String!) {
              addProfileBullet(input: { kind: EXPERIENCE, entryId: $entryId, text: $text }) { profileBullet { id } }
            }
            """, new { entryId, text })).GetProperty("addProfileBullet").GetProperty("profileBullet").GetProperty("id").GetInt32();

    private static async Task<List<JsonElement>> ListAsync(HttpClient client, bool archived = false) =>
        [.. (await client.GraphQLAsync(List, new { archived })).GetProperty("baseCvs").EnumerateArray()];

    private static async Task<JsonElement> VersionAsync(HttpClient client, int id) =>
        (await client.GraphQLAsync("""
            query ($id: Int!) { baseCvVersion(id: $id) { number content { skills { sourceSkillId categoryId } } } }
            """, new { id })).GetProperty("baseCvVersion");

    private static async Task<JsonElement> DocumentsAsync(HttpClient client, int listingId) =>
        (await client.GraphQLAsync("""
            query ($id: Int!) { listingById(id: $id) { documents { kind origin sentAt baseCvVersion { number } } } }
            """, new { id = listingId })).GetProperty("listingById").GetProperty("documents");

    private static async Task<(int Id, string Name)> SetProfileSkillAsync(HttpClient client, string name) =>
        ((await client.GraphQLAsync("""
            mutation ($name: String!) { setProfileSkill(input: { name: $name, level: 3 }) { profileSkill { skill { id } } } }
            """, new { name })).GetProperty("setProfileSkill").GetProperty("profileSkill").GetProperty("skill")
            .GetProperty("id").GetInt32(), name);
}
