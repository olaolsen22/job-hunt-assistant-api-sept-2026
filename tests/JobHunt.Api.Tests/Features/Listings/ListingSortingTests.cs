namespace JobHunt.Api.Tests.Features.Listings;

public class ListingSortingTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Companies_ClientOrder_OverridesDefaultOrder()
    {
        var client = await factory.GetOwnerClientAsync();
        await ListingTests.CreateAsync(client, ListingTests.NewListing(ListingTests.Unique("Acme")));
        await ListingTests.CreateAsync(client, ListingTests.NewListing(ListingTests.Unique("Zeta")));

        var names = (await client.GraphQLAsync("{ companies(first: 50, order: [{ name: DESC }]) { nodes { name } } }"))
            .GetProperty("companies").GetProperty("nodes").EnumerateArray()
            .Select(n => n.GetProperty("name").GetString()!).ToList();

        Assert.Equal(names.OrderDescending(StringComparer.Ordinal), names);
    }

    [Fact]
    public async Task Listings_DefaultOrder_ClosestDeadlineFirst()
    {
        var client = await factory.GetOwnerClientAsync();
        var company = ListingTests.Unique("Acme");
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        string Day(int offset) => today.AddDays(offset).ToString("yyyy-MM-dd");

        async Task Create(string title, string deadlineType, string? postedOn = null, string? deadlineOn = null) =>
            await ListingTests.CreateAsync(client, ListingTests.NewListing(company, title: title,
                deadlineType: deadlineType, postedOn: postedOn ?? Day(-1), deadlineOn: deadlineOn));

        await Create("expired date", "DATE", deadlineOn: Day(-1));
        await Create("undated", "UNSPECIFIED");
        await Create("stale asap", "ASAP", postedOn: Day(-20));
        await Create("later date", "DATE", deadlineOn: Day(30));
        await Create("recent asap", "ASAP", postedOn: Day(-10));
        await Create("soon date", "DATE", deadlineOn: Day(2));

        var titles = (await client.GraphQLAsync(
                "query($company: String!) { listings(where: { company: { name: { eq: $company } } }) { items { title } } }",
                new { company }))
            .GetProperty("listings").GetProperty("items").EnumerateArray()
            .Select(n => n.GetProperty("title").GetString()!).ToList();

        Assert.Equal(["soon date", "recent asap", "later date", "undated", "stale asap", "expired date"], titles);
    }
}
