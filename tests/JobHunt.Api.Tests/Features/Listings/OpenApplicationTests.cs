using System.Text.Json;

namespace JobHunt.Api.Tests.Features.Listings;

public class OpenApplicationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Create = """
        mutation ($listing: ListingInput!) {
          createListing(input: { listing: $listing }) {
            listing { id kind url postedOn dueOn }
            possibleDuplicates { id }
            errors { __typename ... on Error { message } }
          }
        }
        """;

    private static object Unsolicited(string companyName, string title = "Backend Developer",
        string? url = null, string? postedOn = null, string deadlineType = "UNSPECIFIED", string? postingLanguage = null) =>
        new
        {
            kind = "UNSOLICITED", title, companyName, jobRoleName = "Backend Developer", url, postedOn, deadlineType,
            postingLanguage, skills = Array.Empty<object>(), languages = Array.Empty<object>(), locations = Array.Empty<object>(),
        };

    private static async Task<JsonElement> CreateAsync(HttpClient client, object listing) =>
        (await client.GraphQLAsync(Create, new { listing })).GetProperty("createListing");

    private static int IdOf(JsonElement payload) => payload.GetProperty("listing").GetProperty("id").GetInt32();

    private static int[] Duplicates(JsonElement payload) =>
        payload.GetProperty("possibleDuplicates").EnumerateArray().Select(l => l.GetProperty("id").GetInt32()).ToArray();

    [Fact]
    public async Task Unsolicited_HasNoUrlPostedDateOrDue()
    {
        var client = await factory.GetOwnerClientAsync();

        var listing = (await CreateAsync(client, Unsolicited(ListingTests.Unique("Six Robotics")))).GetProperty("listing");

        Assert.Equal("UNSOLICITED", listing.GetProperty("kind").GetString());
        Assert.Equal(JsonValueKind.Null, listing.GetProperty("url").ValueKind);
        Assert.Equal(JsonValueKind.Null, listing.GetProperty("postedOn").ValueKind);
        Assert.Equal(JsonValueKind.Null, listing.GetProperty("dueOn").ValueKind);
    }

    [Fact]
    public async Task Kind_DefaultsToVacancy_AndOpenInvitationNeedsAPage()
    {
        var client = await factory.GetOwnerClientAsync();
        var company = ListingTests.Unique("Deploi");

        var vacancy = await ListingTests.CreateAsync(client, ListingTests.NewListing(company));
        var invitation = (await CreateAsync(client, new
        {
            kind = "OPEN_INVITATION", title = "Developer", companyName = company, jobRoleName = "Developer",
            url = ListingTests.UniqueUrl(), postedOn = "2026-10-01", deadlineType = "ASAP",
            skills = Array.Empty<object>(), languages = Array.Empty<object>(), locations = Array.Empty<object>(),
        })).GetProperty("listing");
        var withoutUrl = await CreateAsync(client, new
        {
            kind = "OPEN_INVITATION", title = "Developer", companyName = company, jobRoleName = "Developer",
            postedOn = "2026-10-01", skills = Array.Empty<object>(), languages = Array.Empty<object>(), locations = Array.Empty<object>(),
        });

        Assert.Equal("VACANCY", (await client.GraphQLAsync("query ($id: Int!) { listingById(id: $id) { kind } }",
            new { id = vacancy.GetProperty("id").GetInt32() })).GetProperty("listingById").GetProperty("kind").GetString());
        Assert.Equal("OPEN_INVITATION", invitation.GetProperty("kind").GetString());
        Assert.Equal("Url must be an http(s) link.", withoutUrl.GetProperty("errors")[0].GetProperty("message").GetString());
    }

    [Theory]
    [InlineData("https://sixrobotics.com/careers", null, "UNSPECIFIED", "no URL")]
    [InlineData(null, "2026-10-01", "UNSPECIFIED", "no posted date")]
    [InlineData(null, null, "ASAP", "no deadline")]
    public async Task Unsolicited_RejectsPostingFields(string? url, string? postedOn, string deadlineType, string message)
    {
        var client = await factory.GetOwnerClientAsync();

        var errors = (await CreateAsync(client, Unsolicited(ListingTests.Unique("Acme"), url: url, postedOn: postedOn,
            deadlineType: deadlineType))).GetProperty("errors");

        Assert.Equal("InvalidListingError", errors[0].GetProperty("__typename").GetString());
        Assert.Contains(message, errors[0].GetProperty("message").GetString());
    }

    [Fact]
    public async Task Vacancy_StillRequiresPostedDate()
    {
        var client = await factory.GetOwnerClientAsync();

        var errors = (await CreateAsync(client, new
        {
            title = "Developer", companyName = ListingTests.Unique("Acme"), jobRoleName = "Developer", url = ListingTests.UniqueUrl(),
            skills = Array.Empty<object>(), languages = Array.Empty<object>(), locations = Array.Empty<object>(),
        })).GetProperty("errors");

        Assert.Equal("Posted date is required.", errors[0].GetProperty("message").GetString());
    }

    [Fact]
    public async Task SecondOpenApplication_IsAllowedButFlagged_UntilTheFirstIsClosed()
    {
        var client = await factory.GetOwnerClientAsync();
        var company = ListingTests.Unique("Six Robotics");

        var first = IdOf(await CreateAsync(client, Unsolicited(company, title: "Backend Developer")));
        // A different role and title, so only the open-application rule can match it.
        var second = await CreateAsync(client, new
        {
            kind = "UNSOLICITED", title = "Platform Engineer", companyName = company, jobRoleName = ListingTests.Unique("Platform Engineer"),
            skills = Array.Empty<object>(), languages = Array.Empty<object>(), locations = Array.Empty<object>(),
        });
        var vacancy = await ListingTests.CreateAsync(client, ListingTests.NewListing(company, title: "Designer",
            jobRoleName: ListingTests.Unique("Designer")));

        Assert.Equal([first], Duplicates(second));
        // An ordinary vacancy at the same company isn't a second open application.
        Assert.DoesNotContain(vacancy.GetProperty("id").GetInt32(), Duplicates(second));

        var cvDocumentId = await Documents.DocumentTests.UploadIdAsync(client, first);
        await client.GraphQLAsync("""
            mutation ($listingId: Int!, $cvDocumentId: Int!) {
              applyToListing(input: { listingId: $listingId, cvDocumentId: $cvDocumentId }) { listing { id } }
            }
            """, new { listingId = first, cvDocumentId });
        await client.GraphQLAsync("""
            mutation ($listingId: Int!) { recordOutcome(input: { listingId: $listingId, outcome: REJECTED }) { listing { id } } }
            """, new { listingId = first });
        var third = await CreateAsync(client, new
        {
            kind = "UNSOLICITED", title = "Data Engineer", companyName = company, jobRoleName = ListingTests.Unique("Data Engineer"),
            skills = Array.Empty<object>(), languages = Array.Empty<object>(), locations = Array.Empty<object>(),
        });

        Assert.Equal([IdOf(second)], Duplicates(third));
    }

    [Fact]
    public async Task OpenApplications_AreFlaggedPerUserOnly()
    {
        var owner = await factory.GetOwnerClientAsync();
        var guest = await factory.GetGuestClientAsync();
        var company = ListingTests.Unique("Six Robotics");
        await CreateAsync(owner, Unsolicited(company));

        var guestPayload = await CreateAsync(guest, Unsolicited(company));

        Assert.Empty(Duplicates(guestPayload));
    }

    [Fact]
    public async Task DefaultOrder_PutsUnsolicitedAfterDatedListings()
    {
        var client = await factory.GetOwnerClientAsync();
        var company = ListingTests.Unique("Acme");
        await CreateAsync(client, Unsolicited(company, title: "unsolicited"));
        await ListingTests.CreateAsync(client, ListingTests.NewListing(company, title: "expired",
            deadlineType: "DATE", postedOn: "2026-01-01", deadlineOn: "2026-01-10"));

        var titles = (await client.GraphQLAsync(
                "query ($company: String!) { listings(where: { company: { name: { eq: $company } } }) { items { title } } }",
                new { company }))
            .GetProperty("listings").GetProperty("items").EnumerateArray()
            .Select(l => l.GetProperty("title").GetString()).ToList();

        Assert.Equal(["expired", "unsolicited"], titles);
    }

    [Fact]
    public async Task ParseListingFromUrl_Unsolicited_ReturnsInvalidRequest_WithoutFetching()
    {
        var client = await factory.GetOwnerClientAsync();
        var listingId = IdOf(await CreateAsync(client, Unsolicited(ListingTests.Unique("Acme"))));
        var requests = factory.Pages.Requests;

        var errors = (await client.GraphQLAsync("""
                mutation ($listingId: Int!) {
                  parseListingFromUrl(input: { listingId: $listingId }) { errors { __typename ... on Error { message } } }
                }
                """, new { listingId }))
            .GetProperty("parseListingFromUrl").GetProperty("errors");

        Assert.Equal("InvalidParseRequestError", errors[0].GetProperty("__typename").GetString());
        Assert.Equal(requests, factory.Pages.Requests);
    }

    [Fact]
    public async Task CoverLetter_UsesOpenApplicationSubjectAndGuidance()
    {
        var client = await factory.GetOwnerClientAsync();
        var listingId = IdOf(await CreateAsync(client, Unsolicited(ListingTests.Unique("Acme"), postingLanguage: "NORWEGIAN")));

        var subject = (await client.GraphQLAsync("""
                query ($listingId: Int!) { coverLetterDraftFromProfile(listingId: $listingId) { subject } }
                """, new { listingId }))
            .GetProperty("coverLetterDraftFromProfile").GetProperty("subject").GetString();
        var instructions = (await client.GraphQLAsync("""
                query ($listingId: Int!) { coverLetterExternalPrompt(listingId: $listingId) { instructions } }
                """, new { listingId }))
            .GetProperty("coverLetterExternalPrompt").GetProperty("instructions").GetString()!;

        Assert.Equal("Åpen søknad – Backend Developer", subject);
        Assert.Contains("there is no advertised vacancy", instructions);
        Assert.Contains("Never claim or imply that the employer", instructions);
        Assert.DoesNotContain("open exactly its url", instructions);
        Assert.Contains("\"kind\":\"UNSOLICITED\"", instructions);
        Assert.Contains("\"companyResearch\":", instructions);
    }

    [Fact]
    public async Task TailoredCvPrompt_IncludesKindAndCompanyResearch()
    {
        var client = await factory.GetOwnerClientAsync();
        var listingId = IdOf(await CreateAsync(client, Unsolicited(ListingTests.Unique("Acme"))));

        var instructions = (await client.GraphQLAsync("""
                query ($listingId: Int!) { tailoredCvExternalPrompt(listingId: $listingId) { instructions } }
                """, new { listingId }))
            .GetProperty("tailoredCvExternalPrompt").GetProperty("instructions").GetString()!;

        Assert.Contains("\"kind\":\"UNSOLICITED\"", instructions);
        Assert.Contains("\"companyResearch\":", instructions);
        Assert.Contains("This is an open application with no posting", instructions);
    }
}
