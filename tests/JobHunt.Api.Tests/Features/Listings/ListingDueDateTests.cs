using System.Net.Http.Json;
using System.Text.Json;

namespace JobHunt.Api.Tests.Features.Listings;

public class ListingDueDateTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private static string Day(int offset) => Today.AddDays(offset).ToString("yyyy-MM-dd");

    [Fact]
    public async Task DueOn_FollowsDeadlineType()
    {
        var client = await factory.GetOwnerClientAsync();
        var company = ListingTests.Unique("Acme");
        await ListingTests.CreateAsync(client, ListingTests.NewListing(company, title: "asap",
            deadlineType: "ASAP", postedOn: "2026-10-01"));
        await ListingTests.CreateAsync(client, ListingTests.NewListing(company, title: "unspecified",
            deadlineType: "UNSPECIFIED", postedOn: "2026-10-01"));
        await ListingTests.CreateAsync(client, ListingTests.NewListing(company, title: "date",
            deadlineType: "DATE", postedOn: "2026-10-01", deadlineOn: "2026-10-20"));

        // Only dueOn selected: the resolver must still get deadlineType/postedOn/deadlineOn from projection.
        var dueOns = (await client.GraphQLAsync(
                "query($company: String!) { listings(where: { company: { name: { eq: $company } } }, order: [{ title: ASC }]) { items { dueOn } } }",
                new { company }))
            .GetProperty("listings").GetProperty("items").EnumerateArray()
            .Select(n => n.GetProperty("dueOn").GetString()!).ToList();

        Assert.Equal(["2026-10-15", "2026-10-20", "2026-10-31"], dueOns);
    }

    [Fact]
    public async Task DueWithinDays_ReturnsDueFromTodayToTodayPlusN()
    {
        var client = await factory.GetOwnerClientAsync();
        var company = ListingTests.Unique("Acme");
        foreach (var (title, offset) in new[] { ("today", 0), ("in 7", 7), ("in 8", 8), ("yesterday", -1) })
            await ListingTests.CreateAsync(client, ListingTests.NewListing(company, title: title,
                deadlineType: "DATE", postedOn: Day(-10), deadlineOn: Day(offset)));

        var titles = await TitlesAsync(client, "dueWithinDays: 7, where: { company: { name: { eq: $company } } }", company);

        Assert.Equal(["today", "in 7"], titles);
    }

    [Fact]
    public async Task DueWithinDays_Negative_ReturnsError()
    {
        var client = await factory.GetOwnerClientAsync();

        var response = await client.PostAsJsonAsync("/graphql",
            new { query = "{ listings(dueWithinDays: -1) { items { id } } }" });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Contains("dueWithinDays", body.GetProperty("errors")[0].GetProperty("message").GetString());
    }

    [Fact]
    public async Task Where_PostedOnRange()
    {
        var client = await factory.GetOwnerClientAsync();
        var company = ListingTests.Unique("Acme");
        await ListingTests.CreateAsync(client, ListingTests.NewListing(company, title: "august", postedOn: "2026-08-31"));
        await ListingTests.CreateAsync(client, ListingTests.NewListing(company, title: "september", postedOn: "2026-09-15"));
        await ListingTests.CreateAsync(client, ListingTests.NewListing(company, title: "october", postedOn: "2026-10-01"));

        var titles = await TitlesAsync(client,
            "where: { company: { name: { eq: $company } }, postedOn: { gte: \"2026-09-01\", lte: \"2026-09-30\" } }", company);

        Assert.Equal(["september"], titles);
    }

    [Fact]
    public async Task Where_DeadlineOnRange_SkipsUndatedListings()
    {
        var client = await factory.GetOwnerClientAsync();
        var company = ListingTests.Unique("Acme");
        await ListingTests.CreateAsync(client, ListingTests.NewListing(company, title: "in range",
            deadlineType: "DATE", postedOn: Day(-1), deadlineOn: Day(5)));
        await ListingTests.CreateAsync(client, ListingTests.NewListing(company, title: "out of range",
            deadlineType: "DATE", postedOn: Day(-1), deadlineOn: Day(20)));
        await ListingTests.CreateAsync(client, ListingTests.NewListing(company, title: "asap",
            deadlineType: "ASAP", postedOn: Day(-1)));
        await ListingTests.CreateAsync(client, ListingTests.NewListing(company, title: "unspecified",
            deadlineType: "UNSPECIFIED", postedOn: Day(-1)));

        var titles = await TitlesAsync(client,
            $"where: {{ company: {{ name: {{ eq: $company }} }}, deadlineOn: {{ gte: \"{Day(0)}\", lte: \"{Day(14)}\" }} }}", company);

        Assert.Equal(["in range"], titles);
    }

    [Fact]
    public async Task Where_PostedAndDeadlineRangesTogether()
    {
        var client = await factory.GetOwnerClientAsync();
        var company = ListingTests.Unique("Acme");
        await ListingTests.CreateAsync(client, ListingTests.NewListing(company, title: "match",
            deadlineType: "DATE", postedOn: "2026-09-15", deadlineOn: "2026-10-20"));
        await ListingTests.CreateAsync(client, ListingTests.NewListing(company, title: "posted too early",
            deadlineType: "DATE", postedOn: "2026-08-15", deadlineOn: "2026-10-20"));
        await ListingTests.CreateAsync(client, ListingTests.NewListing(company, title: "deadline too late",
            deadlineType: "DATE", postedOn: "2026-09-15", deadlineOn: "2026-11-20"));

        var titles = await TitlesAsync(client, """
            where: {
              company: { name: { eq: $company } }
              postedOn: { gte: "2026-09-01", lte: "2026-09-30" }
              deadlineOn: { gte: "2026-10-01", lte: "2026-10-31" }
            }
            """, company);

        Assert.Equal(["match"], titles);
    }

    private static async Task<List<string>> TitlesAsync(HttpClient client, string arguments, string company) =>
        (await client.GraphQLAsync(
                $"query($company: String!) {{ listings({arguments}) {{ items {{ title }} }} }}", new { company }))
            .GetProperty("listings").GetProperty("items").EnumerateArray()
            .Select(n => n.GetProperty("title").GetString()!).ToList();
}
