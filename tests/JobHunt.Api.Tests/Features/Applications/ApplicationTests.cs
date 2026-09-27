using System.Net;
using System.Text.Json;
using JobHunt.Api.Tests.Features.Documents;
using JobHunt.Api.Tests.Features.Listings;

namespace JobHunt.Api.Tests.Features.Applications;

public class ApplicationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string ListingFields = """
        listing {
          status statusChangedAt
          statusHistory(order: { id: ASC }) { id status occurredAt note }
          documents { id kind fileName sentAt }
        }
        errors { __typename ... on Error { message } }
        """;

    private const string Apply = $$"""
        mutation ($listingId: Int!, $cvDocumentId: Int!, $coverLetterDocumentId: Int, $appliedAt: DateTime, $note: String) {
          applyToListing(input: {
            listingId: $listingId, cvDocumentId: $cvDocumentId, coverLetterDocumentId: $coverLetterDocumentId,
            appliedAt: $appliedAt, note: $note
          }) { {{ListingFields}} }
        }
        """;

    private const string RecordOutcome = $$"""
        mutation ($listingId: Int!, $outcome: ApplicationOutcome!, $occurredAt: DateTime) {
          recordOutcome(input: { listingId: $listingId, outcome: $outcome, occurredAt: $occurredAt }) { {{ListingFields}} }
        }
        """;

    private const string UpdateStatusChange = $$"""
        mutation ($id: Int!, $occurredAt: DateTime!, $note: String) {
          updateStatusChange(input: { id: $id, occurredAt: $occurredAt, note: $note }) { {{ListingFields}} }
        }
        """;

    private const string Undo = $$"""
        mutation ($listingId: Int!) {
          undoStatusChange(input: { listingId: $listingId }) { {{ListingFields}} }
        }
        """;

    private static string Iso(DateTimeOffset time) => time.ToString("yyyy-MM-ddTHH:mm:sszzz");

    [Fact]
    public async Task Apply_MarksDocumentsSent_RenamedWithAppliedDate()
    {
        var client = await factory.GetOwnerClientAsync();
        var listingId = await DocumentTests.CreateListingAsync(client, "Nordlys");
        var cvId = await DocumentTests.UploadIdAsync(client, listingId);
        var letterId = await DocumentTests.UploadIdAsync(client, listingId, "COVER_LETTER");
        // Sent with an offset, as the UI does; stored and returned as UTC.
        var appliedAt = new DateTimeOffset(DateTime.UtcNow.Date.AddDays(-3).AddHours(21).Ticks, TimeSpan.FromHours(2));

        var payload = (await client.GraphQLAsync(Apply, new
        {
            listingId, cvDocumentId = cvId, coverLetterDocumentId = letterId, appliedAt = Iso(appliedAt), note = "  **Referred** by Kari  ",
        })).GetProperty("applyToListing");

        var listing = Listing(payload);
        Assert.Equal("APPLIED", listing.GetProperty("status").GetString());
        Assert.Equal(appliedAt.UtcDateTime, listing.GetProperty("statusChangedAt").GetDateTime().ToUniversalTime());
        var change = Assert.Single(listing.GetProperty("statusHistory").EnumerateArray());
        Assert.Equal("**Referred** by Kari", change.GetProperty("note").GetString());
        foreach (var document in listing.GetProperty("documents").EnumerateArray())
        {
            Assert.NotEqual(JsonValueKind.Null, document.GetProperty("sentAt").ValueKind);
            Assert.EndsWith($"_{appliedAt.UtcDateTime:yyyy-MM-dd}.pdf", document.GetProperty("fileName").GetString());
        }
    }

    [Fact]
    public async Task Apply_WithCoverLetterAsCv_ReturnsInvalidDocumentError()
    {
        var client = await factory.GetOwnerClientAsync();
        var listingId = await DocumentTests.CreateListingAsync(client, "Acme");
        var letterId = await DocumentTests.UploadIdAsync(client, listingId, "COVER_LETTER");

        var payload = (await client.GraphQLAsync(Apply, new { listingId, cvDocumentId = letterId }))
            .GetProperty("applyToListing");

        Assert.Equal("InvalidDocumentError", ErrorType(payload));
    }

    [Fact]
    public async Task Apply_WithAnotherListingsCv_ReturnsDocumentNotFoundError()
    {
        var client = await factory.GetOwnerClientAsync();
        var listingId = await DocumentTests.CreateListingAsync(client, "Acme");
        var otherCvId = await DocumentTests.UploadIdAsync(client, await DocumentTests.CreateListingAsync(client, "Other"));

        var payload = (await client.GraphQLAsync(Apply, new { listingId, cvDocumentId = otherCvId }))
            .GetProperty("applyToListing");

        Assert.Equal("DocumentNotFoundError", ErrorType(payload));
    }

    [Fact]
    public async Task Apply_InTheFuture_ReturnsInvalidApplicationError()
    {
        var client = await factory.GetOwnerClientAsync();
        var (listingId, cvId) = await ListingWithCvAsync(client);

        var payload = (await client.GraphQLAsync(Apply,
            new { listingId, cvDocumentId = cvId, appliedAt = Iso(DateTimeOffset.UtcNow.AddDays(1)) }))
            .GetProperty("applyToListing");

        Assert.Equal("InvalidApplicationError", ErrorType(payload));
    }

    [Fact]
    public async Task Apply_Twice_ReturnsInvalidStatusTransitionError()
    {
        var client = await factory.GetOwnerClientAsync();
        var (listingId, cvId) = await ListingWithCvAsync(client);
        await client.GraphQLAsync(Apply, new { listingId, cvDocumentId = cvId });

        var payload = (await client.GraphQLAsync(Apply, new { listingId, cvDocumentId = cvId }))
            .GetProperty("applyToListing");

        Assert.Equal("InvalidStatusTransitionError", ErrorType(payload));
    }

    [Fact]
    public async Task RecordOutcome_RejectedRightAfterApplying()
    {
        var client = await factory.GetOwnerClientAsync();
        var listingId = await AppliedListingAsync(client);

        var payload = (await client.GraphQLAsync(RecordOutcome, new { listingId, outcome = "REJECTED" }))
            .GetProperty("recordOutcome");

        var listing = Listing(payload);
        Assert.Equal("REJECTED", listing.GetProperty("status").GetString());
        Assert.Equal(["APPLIED", "REJECTED"], Statuses(listing));
    }

    [Fact]
    public async Task RecordOutcome_OnSavedListing_ReturnsInvalidStatusTransitionError()
    {
        var client = await factory.GetOwnerClientAsync();
        var listingId = await DocumentTests.CreateListingAsync(client, "Acme");

        var payload = (await client.GraphQLAsync(RecordOutcome, new { listingId, outcome = "OFFER" }))
            .GetProperty("recordOutcome");

        Assert.Equal("InvalidStatusTransitionError", ErrorType(payload));
    }

    [Fact]
    public async Task RecordOutcome_BeforeApplicationDate_ReturnsInvalidApplicationError()
    {
        var client = await factory.GetOwnerClientAsync();
        var listingId = await AppliedListingAsync(client);

        var payload = (await client.GraphQLAsync(RecordOutcome,
            new { listingId, outcome = "REJECTED", occurredAt = Iso(DateTimeOffset.UtcNow.AddDays(-30)) }))
            .GetProperty("recordOutcome");

        Assert.Equal("InvalidApplicationError", ErrorType(payload));
    }

    [Fact]
    public async Task RecordOutcome_WithdrawAfterOffer()
    {
        var client = await factory.GetOwnerClientAsync();
        var listingId = await AppliedListingAsync(client);
        await client.GraphQLAsync(RecordOutcome, new { listingId, outcome = "OFFER" });

        var payload = (await client.GraphQLAsync(RecordOutcome, new { listingId, outcome = "WITHDRAWN" }))
            .GetProperty("recordOutcome");

        Assert.Equal(["APPLIED", "OFFER", "WITHDRAWN"], Statuses(Listing(payload)));
    }

    [Fact]
    public async Task UpdateStatusChange_FixesDateAndNote_AndDocumentNames()
    {
        var client = await factory.GetOwnerClientAsync();
        var listingId = await AppliedListingAsync(client);
        var changeId = await LatestChangeIdAsync(client, listingId);
        var appliedAt = DateTimeOffset.UtcNow.AddDays(-5);

        var payload = (await client.GraphQLAsync(UpdateStatusChange, new { id = changeId, occurredAt = Iso(appliedAt), note = "Via LinkedIn" }))
            .GetProperty("updateStatusChange");

        var listing = Listing(payload);
        var change = Assert.Single(listing.GetProperty("statusHistory").EnumerateArray());
        Assert.Equal("Via LinkedIn", change.GetProperty("note").GetString());
        Assert.Equal(appliedAt.ToString("yyyy-MM-dd"),
            listing.GetProperty("statusChangedAt").GetDateTime().ToUniversalTime().ToString("yyyy-MM-dd"));
        var document = Assert.Single(listing.GetProperty("documents").EnumerateArray());
        Assert.EndsWith($"_{appliedAt:yyyy-MM-dd}.pdf", document.GetProperty("fileName").GetString());
    }

    [Fact]
    public async Task UpdateStatusChange_AppliedAfterLaterStep_ReturnsInvalidApplicationError()
    {
        var client = await factory.GetOwnerClientAsync();
        var (listingId, cvId) = await ListingWithCvAsync(client);
        await client.GraphQLAsync(Apply, new { listingId, cvDocumentId = cvId, appliedAt = Iso(DateTimeOffset.UtcNow.AddDays(-5)) });
        var appliedId = await LatestChangeIdAsync(client, listingId);
        await client.GraphQLAsync(RecordOutcome,
            new { listingId, outcome = "REJECTED", occurredAt = Iso(DateTimeOffset.UtcNow.AddDays(-3)) });

        var payload = (await client.GraphQLAsync(UpdateStatusChange,
            new { id = appliedId, occurredAt = Iso(DateTimeOffset.UtcNow.AddDays(-1)) }))
            .GetProperty("updateStatusChange");

        Assert.Equal("InvalidApplicationError", ErrorType(payload));
    }

    [Fact]
    public async Task Undo_RestoresPreviousStatus()
    {
        var client = await factory.GetOwnerClientAsync();
        var listingId = await AppliedListingAsync(client);
        await client.GraphQLAsync(RecordOutcome, new { listingId, outcome = "REJECTED" });

        var listing = Listing((await client.GraphQLAsync(Undo, new { listingId })).GetProperty("undoStatusChange"));

        Assert.Equal("APPLIED", listing.GetProperty("status").GetString());
        Assert.Equal(["APPLIED"], Statuses(listing));
    }

    [Fact]
    public async Task UndoApplied_UnmarksDocuments_SoTheyCanBeDeleted()
    {
        var client = await factory.GetOwnerClientAsync();
        var (listingId, cvId) = await ListingWithCvAsync(client);
        await client.GraphQLAsync(Apply, new { listingId, cvDocumentId = cvId });
        var blocked = await client.GraphQLAsync(DeleteDocument, new { id = cvId });
        Assert.Equal("DocumentSentError", ErrorType(blocked.GetProperty("deleteDocument")));

        var listing = Listing((await client.GraphQLAsync(Undo, new { listingId })).GetProperty("undoStatusChange"));

        Assert.Equal("SAVED", listing.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, listing.GetProperty("statusChangedAt").ValueKind);
        Assert.Empty(listing.GetProperty("statusHistory").EnumerateArray());
        var deleted = await client.GraphQLAsync(DeleteDocument, new { id = cvId });
        Assert.Equal(cvId, deleted.GetProperty("deleteDocument").GetProperty("id").GetInt32());
    }

    [Fact]
    public async Task Undo_OnSavedListing_ReturnsInvalidStatusTransitionError()
    {
        var client = await factory.GetOwnerClientAsync();
        var listingId = await DocumentTests.CreateListingAsync(client, "Acme");

        var payload = (await client.GraphQLAsync(Undo, new { listingId })).GetProperty("undoStatusChange");

        Assert.Equal("InvalidStatusTransitionError", ErrorType(payload));
    }

    [Fact]
    public async Task DeleteListing_AfterApplying_IsBlocked()
    {
        var client = await factory.GetOwnerClientAsync();
        var listingId = await AppliedListingAsync(client);

        var data = await client.GraphQLAsync("""
            mutation ($id: Int!) { deleteListing(input: { id: $id }) { id errors { __typename } } }
            """, new { id = listingId });

        Assert.Equal("ListingInUseError", ErrorType(data.GetProperty("deleteListing")));
    }

    [Fact]
    public async Task Company_CountsApplications_AndListingsFilterByStatus()
    {
        var client = await factory.GetOwnerClientAsync();
        var company = ListingTests.Unique("Repeat AS");
        var applied = await AppliedListingAsync(client, company);
        await ListingTests.CreateAsync(client, ListingTests.NewListing(company, title: "Frontend Developer"));

        var data = await client.GraphQLAsync("""
            query ($company: String!) {
              companies(search: $company) { nodes { listingCount applicationCount } }
              listings(where: { company: { name: { eq: $company } }, status: { eq: APPLIED } }) { items { id } }
            }
            """, new { company });

        var node = Assert.Single(data.GetProperty("companies").GetProperty("nodes").EnumerateArray());
        Assert.Equal(2, node.GetProperty("listingCount").GetInt32());
        Assert.Equal(1, node.GetProperty("applicationCount").GetInt32());
        var listing = Assert.Single(data.GetProperty("listings").GetProperty("items").EnumerateArray());
        Assert.Equal(applied, listing.GetProperty("id").GetInt32());
    }

    [Fact]
    public async Task OtherUsersListing_IsNotFound()
    {
        var owner = await factory.GetOwnerClientAsync();
        var guest = await factory.GetGuestClientAsync();
        var listingId = await AppliedListingAsync(owner);
        var changeId = await LatestChangeIdAsync(owner, listingId);

        var outcome = await guest.GraphQLAsync(RecordOutcome, new { listingId, outcome = "OFFER" });
        var update = await guest.GraphQLAsync(UpdateStatusChange, new { id = changeId, occurredAt = Iso(DateTimeOffset.UtcNow) });

        Assert.Equal("ListingNotFoundError", ErrorType(outcome.GetProperty("recordOutcome")));
        Assert.Equal("StatusChangeNotFoundError", ErrorType(update.GetProperty("updateStatusChange")));
    }

    private const string DeleteDocument = """
        mutation ($id: Int!) { deleteDocument(input: { id: $id }) { id errors { __typename } } }
        """;

    internal static async Task<int> AppliedListingAsync(HttpClient client, string? company = null)
    {
        var (listingId, cvId) = await ListingWithCvAsync(client, company);
        var payload = (await client.GraphQLAsync(Apply, new { listingId, cvDocumentId = cvId })).GetProperty("applyToListing");
        Assert.Equal(JsonValueKind.Null, payload.GetProperty("errors").ValueKind);
        return listingId;
    }

    private static async Task<(int ListingId, int CvId)> ListingWithCvAsync(HttpClient client, string? company = null)
    {
        var listingId = company is null
            ? await DocumentTests.CreateListingAsync(client, "Acme")
            : (await ListingTests.CreateAsync(client, ListingTests.NewListing(company))).GetProperty("id").GetInt32();
        return (listingId, await DocumentTests.UploadIdAsync(client, listingId));
    }

    private static async Task<int> LatestChangeIdAsync(HttpClient client, int listingId)
    {
        var data = await client.GraphQLAsync("""
            query ($id: Int!) { listingById(id: $id) { statusHistory(order: { id: DESC }) { id } } }
            """, new { id = listingId });
        return data.GetProperty("listingById").GetProperty("statusHistory")[0].GetProperty("id").GetInt32();
    }

    private static JsonElement Listing(JsonElement payload) => payload.GetProperty("listing");

    private static string[] Statuses(JsonElement listing) =>
        listing.GetProperty("statusHistory").EnumerateArray().Select(s => s.GetProperty("status").GetString()!).ToArray();

    internal static string? ErrorType(JsonElement payload) =>
        payload.GetProperty("errors")[0].GetProperty("__typename").GetString();
}
