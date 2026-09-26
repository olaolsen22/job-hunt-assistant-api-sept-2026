using System.Text.Json;
using JobHunt.Api.Tests.Features.Listings;

namespace JobHunt.Api.Tests.Features.Locations;

public class LocationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task CreateListing_ReusesLocationsCaseInsensitivelyAndAllowsCountryOnly()
    {
        var client = await factory.GetOwnerClientAsync();
        var city = ListingTests.Unique("Bergen");

        var first = await ListingTests.CreateAsync(client, ListingTests.NewListing(ListingTests.Unique("Acme"),
            locations: [new { city, countryCode = "NO" }, new { countryCode = "SE" }]));
        var second = await ListingTests.CreateAsync(client, ListingTests.NewListing(ListingTests.Unique("Acme"),
            locations: [new { city = city.ToLowerInvariant(), countryCode = "no" }]));

        var firstLocations = first.GetProperty("locations").EnumerateArray().Select(l => l.GetProperty("location")).ToList();
        var bergen = firstLocations.Single(l => l.GetProperty("city").GetString() == city);
        var sweden = firstLocations.Single(l => l.GetProperty("city").ValueKind == JsonValueKind.Null);
        Assert.Equal("SE", sweden.GetProperty("countryCode").GetString());
        Assert.Equal(bergen.GetProperty("id").GetInt32(),
            second.GetProperty("locations")[0].GetProperty("location").GetProperty("id").GetInt32());
    }

    [Fact]
    public async Task CreateListing_WithUnknownCountryCode_ReturnsInvalidLocationError()
    {
        var client = await factory.GetOwnerClientAsync();

        var data = await client.GraphQLAsync("""
            mutation ($listing: ListingInput!) {
              createListing(input: { listing: $listing }) { errors { __typename } }
            }
            """, new { listing = ListingTests.NewListing(ListingTests.Unique("Acme"),
            locations: [new { city = "Oslo", countryCode = "XX" }]) });

        Assert.Equal("InvalidLocationError",
            data.GetProperty("createListing").GetProperty("errors")[0].GetProperty("__typename").GetString());
    }

    [Fact]
    public async Task TargetLocations_FindListingsInTargetCities()
    {
        var client = await factory.GetOwnerClientAsync();
        var target = ListingTests.Unique("Trondheim");
        var other = ListingTests.Unique("Tromsø");

        // Targeted before any listing mentions it.
        await client.GraphQLAsync("""
            mutation ($city: String!) {
              saveLocation(input: { city: $city, countryCode: "NO", isTarget: true }) { location { id } }
            }
            """, new { city = target });
        var inTarget = await ListingTests.CreateAsync(client, ListingTests.NewListing(ListingTests.Unique("Acme"),
            locations: [new { city = target, countryCode = "NO" }]));
        await ListingTests.CreateAsync(client, ListingTests.NewListing(ListingTests.Unique("Acme"),
            locations: [new { city = other, countryCode = "NO" }]));

        var data = await client.GraphQLAsync("""
            query ($city: String!) {
              locations(search: $city, where: { isTarget: { eq: true } }) { nodes { city listingCount } }
              listings(where: { locations: { some: { location: { isTarget: { eq: true } } } } }) { nodes { id } }
            }
            """, new { city = target });

        var location = Assert.Single(data.GetProperty("locations").GetProperty("nodes").EnumerateArray());
        Assert.Equal(1, location.GetProperty("listingCount").GetInt32());
        var ids = data.GetProperty("listings").GetProperty("nodes").EnumerateArray()
            .Select(n => n.GetProperty("id").GetInt32()).ToList();
        Assert.Contains(inTarget.GetProperty("id").GetInt32(), ids);
        Assert.Single(ids);
    }
}
