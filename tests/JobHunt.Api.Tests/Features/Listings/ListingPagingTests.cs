using System.Text.Json;

namespace JobHunt.Api.Tests.Features.Listings;

// Own factory, so the owner has exactly the listings created here.
public class ListingPagingTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Listings_PageBySkipAndTake_WithTotalCount()
    {
        var client = await factory.GetOwnerClientAsync();
        var prefix = ListingTests.Unique("Paging");
        for (var i = 1; i <= 5; i++)
            await ListingTests.CreateAsync(client, ListingTests.NewListing(ListingTests.Unique("Acme"), title: $"{prefix} {i}"));

        // Page 2 of 2-per-page, sorted by title: items 3 and 4.
        var page = (await client.GraphQLAsync("""
            query ($prefix: String!) {
              listings(skip: 2, take: 2, where: { title: { startsWith: $prefix } }, order: [{ title: ASC }]) {
                items { title }
                pageInfo { hasNextPage hasPreviousPage }
                totalCount
              }
            }
            """, new { prefix })).GetProperty("listings");

        Assert.Equal([$"{prefix} 3", $"{prefix} 4"],
            page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("title").GetString()));
        Assert.Equal(5, page.GetProperty("totalCount").GetInt32());
        Assert.True(page.GetProperty("pageInfo").GetProperty("hasNextPage").GetBoolean());
        Assert.True(page.GetProperty("pageInfo").GetProperty("hasPreviousPage").GetBoolean());
    }

    [Fact]
    public async Task Listings_WithoutArguments_ReturnNewestFirst()
    {
        var client = await factory.GetOwnerClientAsync();
        var older = await ListingTests.CreateAsync(client, ListingTests.NewListing(ListingTests.Unique("Acme")));
        var newer = await ListingTests.CreateAsync(client, ListingTests.NewListing(ListingTests.Unique("Acme")));

        var items = (await client.GraphQLAsync("{ listings { items { id } } }"))
            .GetProperty("listings").GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("id").GetInt32()).ToList();

        Assert.True(items.IndexOf(Id(newer)) < items.IndexOf(Id(older)));
    }

    private static int Id(JsonElement listing) => listing.GetProperty("id").GetInt32();
}
