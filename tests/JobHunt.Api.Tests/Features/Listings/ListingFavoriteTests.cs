using System.Text.Json;
using JobHunt.Api.Tests.Features.Applications;

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
          listings(where: { favoritedAt: { neq: null } }, order: [{ favoritedAt: DESC }]) { items { id } }
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
    public async Task Favorites_SortedByStageRank_PutFurthestAlongFirst()
    {
        var client = await factory.GetOwnerClientAsync();
        var rejected = await ApplicationTests.AppliedListingAsync(client);
        await client.GraphQLAsync("""
            mutation ($listingId: Int!) { recordOutcome(input: { listingId: $listingId, outcome: REJECTED }) { errors { __typename } } }
            """, new { listingId = rejected });
        var interviewing = await ApplicationTests.AppliedListingAsync(client);
        await client.GraphQLAsync("""
            mutation ($listingId: Int!) { addInterview(input: { listingId: $listingId, scheduledAt: "2026-01-05T10:00:00Z" }) { errors { __typename } } }
            """, new { listingId = interviewing });
        var saved = await CreateIdAsync(client);
        int[] mine = [interviewing, saved, rejected];
        foreach (var id in mine)
            await SetAsync(client, id, true);

        var ids = (await client.GraphQLAsync("""
            { listings(where: { favoritedAt: { neq: null } }, order: [{ stageRank: DESC }, { favoritedAt: DESC }]) { items { id status } } }
            """))
            .GetProperty("listings").GetProperty("items").EnumerateArray().Select(n => n.GetProperty("id").GetInt32());

        Assert.Equal(mine, ids.Where(mine.Contains));
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
        (await client.GraphQLAsync(Favorites)).GetProperty("listings").GetProperty("items").EnumerateArray()
        .Select(n => n.GetProperty("id").GetInt32()).ToArray();
}
