using JobHunt.Api.Tests.Features.Documents;

namespace JobHunt.Api.Tests.Features.Applications;

public class InterviewTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string AddInterview = """
        mutation ($listingId: Int!, $scheduledAt: DateTime!, $title: String, $notes: String) {
          addInterview(input: { listingId: $listingId, scheduledAt: $scheduledAt, title: $title, notes: $notes }) {
            interview { id title scheduledAt notes listing { status } }
            errors { __typename ... on Error { message } }
          }
        }
        """;

    private const string UpdateInterview = """
        mutation ($id: Int!, $scheduledAt: DateTime!, $title: String, $notes: String) {
          updateInterview(input: { id: $id, scheduledAt: $scheduledAt, title: $title, notes: $notes }) {
            interview { title notes }
            errors { __typename }
          }
        }
        """;

    private const string DeleteInterview = """
        mutation ($id: Int!) { deleteInterview(input: { id: $id }) { id errors { __typename } } }
        """;

    private const string Undo = """
        mutation ($listingId: Int!) { undoStatusChange(input: { listingId: $listingId }) { listing { status } errors { __typename } } }
        """;

    [Fact]
    public async Task FirstInterview_MovesToInterviewing_AndRoundsSortByTime()
    {
        var client = await factory.GetOwnerClientAsync();
        var listingId = await ApplicationTests.AppliedListingAsync(client);

        var first = (await client.GraphQLAsync(AddInterview,
            new { listingId, scheduledAt = "2026-10-08T10:00:00+02:00", title = "Tech round", notes = "- Bring **laptop**" }))
            .GetProperty("addInterview").GetProperty("interview");
        await client.GraphQLAsync(AddInterview, new { listingId, scheduledAt = "2026-10-01T14:30:00+02:00", title = "HR screen" });

        Assert.Equal("INTERVIEWING", first.GetProperty("listing").GetProperty("status").GetString());
        Assert.Equal(new DateTime(2026, 10, 8, 8, 0, 0, DateTimeKind.Utc),
            first.GetProperty("scheduledAt").GetDateTime().ToUniversalTime());
        var data = await client.GraphQLAsync("""
            query ($id: Int!) {
              listingById(id: $id) {
                status
                statusHistory(order: { id: ASC }) { status }
                interviews(order: { scheduledAt: ASC }) { title }
              }
            }
            """, new { id = listingId });
        var listing = data.GetProperty("listingById");
        Assert.Equal(["APPLIED", "INTERVIEWING"],
            listing.GetProperty("statusHistory").EnumerateArray().Select(s => s.GetProperty("status").GetString()));
        Assert.Equal(["HR screen", "Tech round"],
            listing.GetProperty("interviews").EnumerateArray().Select(i => i.GetProperty("title").GetString()));
    }

    [Fact]
    public async Task AddInterview_BeforeApplying_ReturnsInvalidStatusTransitionError()
    {
        var client = await factory.GetOwnerClientAsync();
        var listingId = await DocumentTests.CreateListingAsync(client, "Acme");

        var payload = (await client.GraphQLAsync(AddInterview, new { listingId, scheduledAt = "2026-10-01T10:00:00Z" }))
            .GetProperty("addInterview");

        Assert.Equal("InvalidStatusTransitionError", ApplicationTests.ErrorType(payload));
    }

    [Fact]
    public async Task AddInterview_WithTooLongNotes_ReturnsInvalidInterviewError()
    {
        var client = await factory.GetOwnerClientAsync();
        var listingId = await ApplicationTests.AppliedListingAsync(client);

        var payload = (await client.GraphQLAsync(AddInterview,
            new { listingId, scheduledAt = "2026-10-01T10:00:00Z", notes = new string('x', 10_001) }))
            .GetProperty("addInterview");

        Assert.Equal("InvalidInterviewError", ApplicationTests.ErrorType(payload));
    }

    [Fact]
    public async Task UpdateInterview_ReplacesFields_BlankNotesBecomeNull()
    {
        var client = await factory.GetOwnerClientAsync();
        var listingId = await ApplicationTests.AppliedListingAsync(client);
        var id = await AddAsync(client, listingId);

        var interview = (await client.GraphQLAsync(UpdateInterview,
            new { id, scheduledAt = "2026-10-02T10:00:00Z", title = " Final round ", notes = "   " }))
            .GetProperty("updateInterview").GetProperty("interview");

        Assert.Equal("Final round", interview.GetProperty("title").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, interview.GetProperty("notes").ValueKind);
    }

    [Fact]
    public async Task UndoInterviewing_IsBlockedUntilInterviewsAreDeleted()
    {
        var client = await factory.GetOwnerClientAsync();
        var listingId = await ApplicationTests.AppliedListingAsync(client);
        var id = await AddAsync(client, listingId);

        var blocked = (await client.GraphQLAsync(Undo, new { listingId })).GetProperty("undoStatusChange");
        Assert.Equal("InvalidStatusTransitionError", ApplicationTests.ErrorType(blocked));

        var deleted = await client.GraphQLAsync(DeleteInterview, new { id });
        Assert.Equal(id, deleted.GetProperty("deleteInterview").GetProperty("id").GetInt32());
        var undone = (await client.GraphQLAsync(Undo, new { listingId })).GetProperty("undoStatusChange");
        Assert.Equal("APPLIED", undone.GetProperty("listing").GetProperty("status").GetString());
    }

    [Fact]
    public async Task OtherUsersInterview_IsNotFound()
    {
        var owner = await factory.GetOwnerClientAsync();
        var guest = await factory.GetGuestClientAsync();
        var listingId = await ApplicationTests.AppliedListingAsync(owner);
        var id = await AddAsync(owner, listingId);

        var add = await guest.GraphQLAsync(AddInterview, new { listingId, scheduledAt = "2026-10-01T10:00:00Z" });
        var update = await guest.GraphQLAsync(UpdateInterview, new { id, scheduledAt = "2026-10-01T10:00:00Z" });
        var delete = await guest.GraphQLAsync(DeleteInterview, new { id });

        Assert.Equal("ListingNotFoundError", ApplicationTests.ErrorType(add.GetProperty("addInterview")));
        Assert.Equal("InterviewNotFoundError", ApplicationTests.ErrorType(update.GetProperty("updateInterview")));
        Assert.Equal("InterviewNotFoundError", ApplicationTests.ErrorType(delete.GetProperty("deleteInterview")));
    }

    private static async Task<int> AddAsync(HttpClient client, int listingId) =>
        (await client.GraphQLAsync(AddInterview, new { listingId, scheduledAt = "2026-10-01T10:00:00Z", title = "HR screen" }))
            .GetProperty("addInterview").GetProperty("interview").GetProperty("id").GetInt32();
}
