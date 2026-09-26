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
}
