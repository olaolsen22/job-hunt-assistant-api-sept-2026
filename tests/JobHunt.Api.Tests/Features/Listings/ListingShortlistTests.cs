using System.Text.Json;

namespace JobHunt.Api.Tests.Features.Listings;

// Own factory, so the shortlist only holds listings created by these tests.
public class ListingShortlistTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string SetShortlisted = """
        mutation ($id: Int!, $isShortlisted: Boolean!) {
          setListingShortlisted(input: { id: $id, isShortlisted: $isShortlisted }) {
            listing { id shortlistedAt favoritedAt updatedAt status company { name } }
            errors { __typename }
          }
        }
        """;

    private const string Shortlist = """
        {
          listings(where: { shortlistedAt: { neq: null } }, order: [{ shortlistedAt: DESC }]) { items { id } }
        }
        """;

    [Fact]
    public async Task SetListingShortlisted_BuildsShortlistNewestFirst()
    {
        var client = await factory.GetOwnerClientAsync();
        var first = await CreateIdAsync(client);
        var second = await CreateIdAsync(client);
        await CreateIdAsync(client);

        var favorite = (await client.GraphQLAsync("""
            mutation ($id: Int!) {
              setListingFavorite(input: { id: $id, isFavorite: true }) { listing { favoritedAt updatedAt } }
            }
            """, new { id = first })).GetProperty("setListingFavorite").GetProperty("listing");
        var shortlisted = await SetAsync(client, first, true);
        Assert.Equal(favorite.GetProperty("favoritedAt").GetString(), shortlisted.GetProperty("favoritedAt").GetString());
        Assert.Equal(favorite.GetProperty("updatedAt").GetString(), shortlisted.GetProperty("updatedAt").GetString());
        Assert.Equal("SAVED", shortlisted.GetProperty("status").GetString());
        Assert.NotEqual(JsonValueKind.Null, shortlisted.GetProperty("shortlistedAt").ValueKind);
        Assert.NotNull(shortlisted.GetProperty("company").GetProperty("name").GetString());
        await SetAsync(client, second, true);

        Assert.Equal([second, first], await ShortlistIdsAsync(client));

        // Repeating the action keeps its time; clearing it removes the job from the shortlist.
        var again = await SetAsync(client, first, true);
        Assert.Equal(shortlisted.GetProperty("shortlistedAt").GetString(), again.GetProperty("shortlistedAt").GetString());
        var removed = await SetAsync(client, second, false);
        Assert.Equal(JsonValueKind.Null, removed.GetProperty("shortlistedAt").ValueKind);
        Assert.Equal([first], await ShortlistIdsAsync(client));
        var cleared = await SetAsync(client, first, false);
        Assert.Equal(favorite.GetProperty("favoritedAt").GetString(), cleared.GetProperty("favoritedAt").GetString());
    }

    [Fact]
    public async Task SetListingShortlisted_OnOtherUsersListing_ReturnsListingNotFoundError()
    {
        var owner = await factory.GetOwnerClientAsync();
        var guest = await factory.GetGuestClientAsync();
        var id = await CreateIdAsync(owner);

        await SetAsync(owner, id, true);
        Assert.Empty(await ShortlistIdsAsync(guest));
        var missing = await owner.GraphQLAsync(SetShortlisted, new { id = int.MaxValue, isShortlisted = true });
        Assert.Equal("ListingNotFoundError", missing.GetProperty("setListingShortlisted").GetProperty("errors")[0].GetProperty("__typename").GetString());
        var data = await guest.GraphQLAsync(SetShortlisted, new { id, isShortlisted = true });

        Assert.Equal("ListingNotFoundError",
            data.GetProperty("setListingShortlisted").GetProperty("errors")[0].GetProperty("__typename").GetString());
        await SetAsync(owner, id, false);
    }

    private static async Task<int> CreateIdAsync(HttpClient client) =>
        (await ListingTests.CreateAsync(client, ListingTests.NewListing(ListingTests.Unique("Acme"))))
        .GetProperty("id").GetInt32();

    private static async Task<JsonElement> SetAsync(HttpClient client, int id, bool isShortlisted) =>
        (await client.GraphQLAsync(SetShortlisted, new { id, isShortlisted }))
        .GetProperty("setListingShortlisted").GetProperty("listing");

    private static async Task<int[]> ShortlistIdsAsync(HttpClient client) =>
        (await client.GraphQLAsync(Shortlist)).GetProperty("listings").GetProperty("items").EnumerateArray()
        .Select(n => n.GetProperty("id").GetInt32()).ToArray();
}
