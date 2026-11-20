using System.Text.Json;

namespace JobHunt.Api.Tests.Features.Listings;

public class ListingOriginalDescriptionTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string English = "Role:\n- Build ENGLISH_TEXT services";
    private const string Norwegian = "Rolle:\n- Bygge NORSK_TEKST tjenester";

    private const string Create = """
        mutation ($listing: ListingInput!) {
          createListing(input: { listing: $listing }) {
            listing { id description originalDescription }
            errors { __typename ... on Error { message } }
          }
        }
        """;

    private const string Update = """
        mutation ($id: Int!, $listing: ListingInput!) {
          updateListing(input: { id: $id, listing: $listing }) {
            listing { description originalDescription }
            errors { __typename ... on Error { message } }
          }
        }
        """;

    [Fact]
    public async Task NorwegianListing_KeepsBothDescriptions_AndOmittedKeepsOriginal()
    {
        var client = await factory.GetOwnerClientAsync();
        var created = await CreateAsync(client, "NORWEGIAN", $"  {Norwegian}  ");
        Assert.Equal(English, created.GetProperty("description").GetString());
        Assert.Equal(Norwegian, created.GetProperty("originalDescription").GetString());

        // A client that doesn't send the field leaves it alone.
        var updated = (await client.GraphQLAsync(Update, new { id = created.GetProperty("id").GetInt32(), listing = Listing("NORWEGIAN") }))
            .GetProperty("updateListing").GetProperty("listing");
        Assert.Equal(Norwegian, updated.GetProperty("originalDescription").GetString());

        var cleared = (await client.GraphQLAsync(Update, new { id = created.GetProperty("id").GetInt32(), listing = Listing("NORWEGIAN", "  ") }))
            .GetProperty("updateListing").GetProperty("listing");
        Assert.Equal(JsonValueKind.Null, cleared.GetProperty("originalDescription").ValueKind);
    }

    [Theory]
    [InlineData("ENGLISH")]
    [InlineData(null)]
    public async Task EnglishOrUnknownListing_DropsOriginal(string? language)
    {
        var client = await factory.GetOwnerClientAsync();
        var created = await CreateAsync(client, language, Norwegian);
        Assert.Equal(JsonValueKind.Null, created.GetProperty("originalDescription").ValueKind);
    }

    [Fact]
    public async Task TooLongOriginal_IsRejected()
    {
        var client = await factory.GetOwnerClientAsync();
        var payload = (await client.GraphQLAsync(Create, new { listing = Listing("NORWEGIAN", new string('x', 20_001)) }))
            .GetProperty("createListing");
        Assert.Equal("Original description is longer than 20000 characters.",
            payload.GetProperty("errors")[0].GetProperty("message").GetString());
    }

    [Fact]
    public async Task Prompts_UseDescriptionInTheirOutputLanguage()
    {
        var client = await factory.GetOwnerClientAsync();
        var id = (await CreateAsync(client, "NORWEGIAN", Norwegian)).GetProperty("id").GetInt32();

        // Cover letter and requirement map follow the posting language here; the CV can be asked for in either.
        var letter = await InstructionsAsync(client, "coverLetterExternalPrompt(listingId: $listingId)", id);
        var map = await InstructionsAsync(client, "requirementMapExternalPrompt(listingId: $listingId)", id);
        var norwegianCv = await InstructionsAsync(client, "tailoredCvExternalPrompt(listingId: $listingId, language: NORWEGIAN)", id);
        var englishCv = await InstructionsAsync(client, "tailoredCvExternalPrompt(listingId: $listingId, language: ENGLISH)", id);

        foreach (var prompt in new[] { letter, map, norwegianCv })
        {
            Assert.Contains("NORSK_TEKST", prompt);
            Assert.DoesNotContain("ENGLISH_TEXT", prompt);
        }
        Assert.Contains("ENGLISH_TEXT", englishCv);
        Assert.DoesNotContain("NORSK_TEKST", englishCv);
    }

    [Fact]
    public async Task Prompts_FallBackToEnglish_WhenNoOriginal()
    {
        var client = await factory.GetOwnerClientAsync();
        var id = (await CreateAsync(client, "NORWEGIAN", null)).GetProperty("id").GetInt32();

        var cv = await InstructionsAsync(client, "tailoredCvExternalPrompt(listingId: $listingId, language: NORWEGIAN)", id);
        Assert.Contains("ENGLISH_TEXT", cv);
    }

    [Fact]
    public async Task OtherUser_CannotSeeListing()
    {
        var owner = await factory.GetOwnerClientAsync();
        var guest = await factory.GetGuestClientAsync();
        var id = (await CreateAsync(owner, "NORWEGIAN", Norwegian)).GetProperty("id").GetInt32();

        var data = await guest.GraphQLAsync("query ($id: Int!) { listingById(id: $id) { originalDescription } }", new { id });
        Assert.Equal(JsonValueKind.Null, data.GetProperty("listingById").ValueKind);
    }

    private static async Task<JsonElement> CreateAsync(HttpClient client, string? language, string? original)
    {
        var payload = (await client.GraphQLAsync(Create, new { listing = Listing(language, original) })).GetProperty("createListing");
        Assert.Equal(JsonValueKind.Null, payload.GetProperty("errors").ValueKind);
        return payload.GetProperty("listing");
    }

    private static async Task<string> InstructionsAsync(HttpClient client, string field, int listingId)
    {
        var data = await client.GraphQLAsync($"query ($listingId: Int!) {{ prompt: {field} {{ instructions }} }}", new { listingId });
        return data.GetProperty("prompt").GetProperty("instructions").GetString()!;
    }

    // Dictionaries, so a test can leave originalDescription out entirely.
    private static Dictionary<string, object?> Listing(string? language) => new()
    {
        ["title"] = "Utvikler", ["companyName"] = ListingTests.Unique("Original description company"),
        ["jobRoleName"] = "Developer", ["description"] = English, ["url"] = ListingTests.UniqueUrl(),
        ["postedOn"] = "2026-10-01", ["deadlineType"] = "UNSPECIFIED", ["postingLanguage"] = language,
        ["skills"] = Array.Empty<object>(), ["languages"] = Array.Empty<object>(), ["locations"] = Array.Empty<object>(),
    };

    private static Dictionary<string, object?> Listing(string? language, string? original)
    {
        var listing = Listing(language);
        listing["originalDescription"] = original;
        return listing;
    }
}
