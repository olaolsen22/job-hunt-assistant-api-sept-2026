using System.Text.Json;

namespace JobHunt.Api.Tests.Features.Listings;

// Own factory, so the favorites list only holds what these tests favorite.
public class ListingFavoriteTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string SetFavorite = """
        mutation ($id: Int!, $isFavorite: Boolean!) {
          setListingFavorite(input: { id: $id, isFavorite: $isFavorite }) {
            listing { id favoritedAt company { name } }
            errors { __typename }
          }
        }
        """;

    private const string Favorites = """
        {
          listings(where: { favoritedAt: { neq: null } }, order: [{ favoritedAt: DESC }]) { nodes { id } }
        }
        """;

    [Fact]
    public async Task SetListingFavorite_BuildsShortlistNewestFirst()
    {
        var client = await factory.GetOwnerClientAsync();
        var first = await CreateIdAsync(client);
        var second = await CreateIdAsync(client);
        await CreateIdAsync(client);

        var favorited = await SetAsync(client, first, true);
        Assert.NotEqual(JsonValueKind.Null, favorited.GetProperty("favoritedAt").ValueKind);
        Assert.NotNull(favorited.GetProperty("company").GetProperty("name").GetString());
        await SetAsync(client, second, true);

        Assert.Equal([second, first], await FavoriteIdsAsync(client));

        // Favoriting again keeps the original time; unfavoriting removes it from the shortlist.
        var again = await SetAsync(client, first, true);
        Assert.Equal(favorited.GetProperty("favoritedAt").GetString(), again.GetProperty("favoritedAt").GetString());
        var removed = await SetAsync(client, second, false);
        Assert.Equal(JsonValueKind.Null, removed.GetProperty("favoritedAt").ValueKind);
        Assert.Equal([first], await FavoriteIdsAsync(client));
    }

    [Fact]
    public async Task SetListingFavorite_OnOtherUsersListing_ReturnsListingNotFoundError()
    {
        var owner = await factory.GetOwnerClientAsync();
        var guest = await factory.GetGuestClientAsync();
        var id = await CreateIdAsync(owner);

        var data = await guest.GraphQLAsync(SetFavorite, new { id, isFavorite = true });

        Assert.Equal("ListingNotFoundError",
            data.GetProperty("setListingFavorite").GetProperty("errors")[0].GetProperty("__typename").GetString());
    }

    private static async Task<int> CreateIdAsync(HttpClient client) =>
        (await ListingTests.CreateAsync(client, ListingTests.NewListing(ListingTests.Unique("Acme"))))
        .GetProperty("id").GetInt32();

    private static async Task<JsonElement> SetAsync(HttpClient client, int id, bool isFavorite) =>
        (await client.GraphQLAsync(SetFavorite, new { id, isFavorite }))
        .GetProperty("setListingFavorite").GetProperty("listing");

    private static async Task<int[]> FavoriteIdsAsync(HttpClient client) =>
        (await client.GraphQLAsync(Favorites)).GetProperty("listings").GetProperty("nodes").EnumerateArray()
        .Select(n => n.GetProperty("id").GetInt32()).ToArray();
}
