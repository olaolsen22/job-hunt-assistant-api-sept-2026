using System.Text.Json;
using JobHunt.Api.Tests.Features.Profiles;

namespace JobHunt.Api.Tests.Features.TailoredCvDrafts;

public class TailoredCvDraftTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Save = """
        mutation ($listingId: Int!, $content: TailoredCvContentInput!) {
          saveTailoredCvDraft(input: { listingId: $listingId, content: $content }) {
            tailoredCvDraft { id createdAt updatedAt content { summary experiences { employer title bullets { text sourceBulletId } } skills { name level } } }
            errors { __typename ... on Error { message } }
          }
        }
        """;

    [Fact]
    public async Task Save_CreatesAndUpdatesDraftWithoutApplyingListing()
    {
        var client = await factory.GetOwnerClientAsync();
        var listingId = await CreateListingAsync(client);
        var first = await SaveAsync(client, listingId, "First draft");
        var created = first.GetProperty("tailoredCvDraft");
        Assert.Equal("First draft", created.GetProperty("content").GetProperty("summary").GetString());

        var second = await SaveAsync(client, listingId, "Updated draft");
        var updated = second.GetProperty("tailoredCvDraft");
        Assert.Equal(created.GetProperty("id").GetInt32(), updated.GetProperty("id").GetInt32());
        Assert.Equal("Updated draft", updated.GetProperty("content").GetProperty("summary").GetString());

        var listing = await client.GraphQLAsync("""
            query ($id: Int!) { listingById(id: $id) { status documents { id sentAt } } }
            """, new { id = listingId });
        var savedListing = listing.GetProperty("listingById");
        Assert.Equal("SAVED", savedListing.GetProperty("status").GetString());
        Assert.Empty(savedListing.GetProperty("documents").EnumerateArray());
    }

    [Fact]
    public async Task Draft_IsReadableOnlyByItsOwner()
    {
        var owner = await factory.GetOwnerClientAsync();
        var listingId = await CreateListingAsync(owner);
        await SaveAsync(owner, listingId, "Private draft");

        var guest = await factory.GetGuestClientAsync();
        var data = await guest.GraphQLAsync("""
            query ($listingId: Int!) {
              tailoredCvDraft(listingId: $listingId) { id content { summary } }
            }
            """, new { listingId });
        Assert.Equal(JsonValueKind.Null, data.GetProperty("tailoredCvDraft").ValueKind);

        var denied = await SaveAsync(guest, listingId, "Foreign draft");
        Assert.Equal("ListingNotFoundError", denied.GetProperty("errors")[0].GetProperty("__typename").GetString());
    }

    [Fact]
    public async Task Save_PreservesUserEditedTextWithSourceBulletAttribution()
    {
        var client = await factory.GetOwnerClientAsync();
        var experience = await ProfileEntryTests.AddExperienceAsync(client, employer: "Source Employer", skills: []);
        var entryId = experience.GetProperty("id").GetInt32();
        var bulletData = await client.GraphQLAsync("""
            mutation ($entryId: Int!) {
              addProfileBullet(input: { kind: EXPERIENCE, entryId: $entryId, text: "Original source bullet" }) {
                profileBullet { id }
              }
            }
            """, new { entryId });
        var bulletId = bulletData.GetProperty("addProfileBullet").GetProperty("profileBullet").GetProperty("id").GetInt32();
        var listingId = await CreateListingAsync(client);

        var data = await client.GraphQLAsync("""
            mutation ($listingId: Int!, $content: TailoredCvContentInput!) {
              saveTailoredCvDraft(input: { listingId: $listingId, content: $content }) {
                tailoredCvDraft {
                  content { experiences { sourceEntryId employer title bullets { sourceBulletId text } } }
                }
                errors { __typename ... on Error { message } }
              }
            }
            """, new
        {
            listingId,
            content = new
            {
                experiences = new[] { new
                {
                    sourceEntryId = entryId, employer = "Source Employer", title = "Developer",
                    bullets = new[] { new { sourceBulletId = (int?)bulletId, text = "Edited wording for this application" } },
                } },
            },
        });

        var payload = data.GetProperty("saveTailoredCvDraft");
        Assert.Equal(JsonValueKind.Null, payload.GetProperty("errors").ValueKind);
        var saved = payload.GetProperty("tailoredCvDraft").GetProperty("content").GetProperty("experiences")[0];
        Assert.Equal(entryId, saved.GetProperty("sourceEntryId").GetInt32());
        Assert.Equal(bulletId, saved.GetProperty("bullets")[0].GetProperty("sourceBulletId").GetInt32());
        Assert.Equal("Edited wording for this application", saved.GetProperty("bullets")[0].GetProperty("text").GetString());
    }

    private const string SaveContent = """
        mutation ($listingId: Int!, $content: TailoredCvContentInput!) {
          saveTailoredCvDraft(input: { listingId: $listingId, content: $content }) {
            tailoredCvDraft { content { language address dateOfBirth referencesOnRequest
              experiences { location } languages { name written spoken } } }
            errors { __typename ... on Error { message } }
          }
        }
        """;

    [Fact]
    public async Task Save_DefaultsLanguageToListingPostingLanguage_AndKeepsAnExplicitChoice()
    {
        var client = await factory.GetOwnerClientAsync();
        var listingId = await CreateListingAsync(client, postingLanguage: "NORWEGIAN");

        var defaulted = await client.GraphQLAsync(SaveContent, new { listingId, content = new { summary = "x" } });
        Assert.Equal("NORWEGIAN", defaulted.GetProperty("saveTailoredCvDraft").GetProperty("tailoredCvDraft")
            .GetProperty("content").GetProperty("language").GetString());

        var chosen = await client.GraphQLAsync(SaveContent, new { listingId, content = new { summary = "x", language = "ENGLISH" } });
        Assert.Equal("ENGLISH", chosen.GetProperty("saveTailoredCvDraft").GetProperty("tailoredCvDraft")
            .GetProperty("content").GetProperty("language").GetString());

        var unset = await client.GraphQLAsync(SaveContent, new { listingId = await CreateListingAsync(client), content = new { summary = "x" } });
        Assert.Equal("ENGLISH", unset.GetProperty("saveTailoredCvDraft").GetProperty("tailoredCvDraft")
            .GetProperty("content").GetProperty("language").GetString());
    }

    [Fact]
    public async Task Save_StoresNavFields()
    {
        var client = await factory.GetOwnerClientAsync();
        var listingId = await CreateListingAsync(client);

        var data = await client.GraphQLAsync(SaveContent, new
        {
            listingId,
            content = new
            {
                address = " Storgata 1, 0155 Oslo ", dateOfBirth = "1990-06-15", referencesOnRequest = true,
                experiences = new[] { new { employer = "Acme", title = "Dev", location = "Lagos, Nigeria", bullets = Array.Empty<object>() } },
                languages = new[] { new { name = "Filipino/Tagalog", written = "VERY_GOOD", spoken = "NATIVE" } },
            },
        });

        var content = data.GetProperty("saveTailoredCvDraft").GetProperty("tailoredCvDraft").GetProperty("content");
        Assert.Equal("Storgata 1, 0155 Oslo", content.GetProperty("address").GetString());
        Assert.Equal("1990-06-15", content.GetProperty("dateOfBirth").GetString());
        Assert.True(content.GetProperty("referencesOnRequest").GetBoolean());
        Assert.Equal("Lagos, Nigeria", content.GetProperty("experiences")[0].GetProperty("location").GetString());
        var language = content.GetProperty("languages")[0];
        Assert.Equal("Filipino/Tagalog", language.GetProperty("name").GetString());
        Assert.Equal("VERY_GOOD", language.GetProperty("written").GetString());
        Assert.Equal("NATIVE", language.GetProperty("spoken").GetString());
    }

    [Theory]
    [InlineData("English", "english")]
    [InlineData("English", " ")]
    public async Task Save_RejectsDuplicateOrBlankLanguageNames(string first, string second)
    {
        var client = await factory.GetOwnerClientAsync();
        var listingId = await CreateListingAsync(client);

        var data = await client.GraphQLAsync(SaveContent, new
        {
            listingId,
            content = new { languages = new[] { new { name = first, written = "GOOD", spoken = "GOOD" },
                new { name = second, written = "GOOD", spoken = "GOOD" } } },
        });

        var payload = data.GetProperty("saveTailoredCvDraft");
        Assert.Equal("InvalidTailoredCvDraftError", payload.GetProperty("errors")[0].GetProperty("__typename").GetString());
    }

    private async Task<JsonElement> SaveAsync(HttpClient client, int listingId, string summary)
    {
        var data = await client.GraphQLAsync(Save, new
        {
            listingId,
            content = new { summary, experiences = Array.Empty<object>(), educations = Array.Empty<object>(),
                certifications = Array.Empty<object>(), projects = Array.Empty<object>(), skills = Array.Empty<object>(),
                languages = Array.Empty<object>() },
        });
        return data.GetProperty("saveTailoredCvDraft");
    }

    private static async Task<int> CreateListingAsync(HttpClient client, string? postingLanguage = null)
    {
        var listing = new
        {
            title = "Tailored CV test", companyName = "Tailored CV test company", jobRoleName = "Developer",
            url = $"https://tailored-cv-test.example/{Guid.NewGuid()}", postedOn = "2026-10-01",
            deadlineType = "UNSPECIFIED", skills = Array.Empty<object>(), languages = Array.Empty<object>(),
            locations = Array.Empty<object>(), postingLanguage,
        };
        var data = await client.GraphQLAsync("""
            mutation ($listing: ListingInput!) { createListing(input: { listing: $listing }) { listing { id } } }
            """, new { listing });
        return data.GetProperty("createListing").GetProperty("listing").GetProperty("id").GetInt32();
    }
}
