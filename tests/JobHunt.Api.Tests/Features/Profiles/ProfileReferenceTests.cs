using System.Text.Json;
using JobHunt.Api.Tests.Features.Applications;

namespace JobHunt.Api.Tests.Features.Profiles;

// The guest's references are cleared at the start of each test: there's a 10-reference limit per profile.
public class ProfileReferenceTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Fields = "id name relationship company email phone note position";

    private const string AddReference = $$"""
        mutation ($name: String!, $relationship: String!, $company: String, $email: String, $note: String) {
          addProfileReference(input: { name: $name, relationship: $relationship, company: $company, email: $email,
                                       note: $note }) {
            profileReference { {{Fields}} }
            errors { __typename }
          }
        }
        """;

    private const string UpdateReference = $$"""
        mutation ($id: Int!, $name: String!, $relationship: String!) {
          updateProfileReference(input: { id: $id, name: $name, relationship: $relationship }) {
            profileReference { {{Fields}} }
            errors { __typename }
          }
        }
        """;

    private const string MoveReference = """
        mutation ($id: Int!, $position: Int!) {
          moveProfileReference(input: { id: $id, position: $position }) {
            references { id position }
            errors { __typename }
          }
        }
        """;

    private const string DeleteReference = """
        mutation ($id: Int!) { deleteProfileReference(input: { id: $id }) { id errors { __typename } } }
        """;

    private static async Task<HttpClient> CleanGuestAsync(ApiFactory factory)
    {
        var client = await factory.GetGuestClientAsync();
        foreach (var id in await IdsAsync(client))
            await client.GraphQLAsync(DeleteReference, new { id });
        return client;
    }

    private static async Task<int[]> IdsAsync(HttpClient client) =>
        [.. (await client.GraphQLAsync("{ profile { references { id } } }")).GetProperty("profile").GetProperty("references")
            .EnumerateArray().Select(r => r.GetProperty("id").GetInt32())];

    private static async Task<JsonElement> AddAsync(HttpClient client, string name, string relationship = "Manager",
        string? email = null) =>
        (await client.GraphQLAsync(AddReference, new { name, relationship, company = (string?)null, email, note = (string?)null }))
        .GetProperty("addProfileReference");

    private static async Task<int> AddOkAsync(HttpClient client, string name) =>
        (await AddAsync(client, name)).GetProperty("profileReference").GetProperty("id").GetInt32();

    private static async Task<string[]> ListAsync(HttpClient client) =>
        [.. (await client.GraphQLAsync("{ profile { references { name position } } }")).GetProperty("profile")
            .GetProperty("references").EnumerateArray().Select((r, i) =>
            {
                Assert.Equal(i, r.GetProperty("position").GetInt32());
                return r.GetProperty("name").GetString()!;
            })];

    [Fact]
    public async Task Add_AppendsInOrder_AndTrims()
    {
        var client = await CleanGuestAsync(factory);

        var first = (await AddAsync(client, " Kari Nordmann ", "Former manager", "kari@example.com"))
            .GetProperty("profileReference");
        await AddAsync(client, "Per Hansen");

        Assert.Equal("Kari Nordmann", first.GetProperty("name").GetString());
        Assert.Equal(["Kari Nordmann", "Per Hansen"], await ListAsync(client));
    }

    public static TheoryData<string, string, string, string?> InvalidReferences => new()
    {
        { "blank relationship", "Kari", " ", null },
        { "blank name", " ", "Manager", null },
        { "bad email", "Kari", "Manager", "not-an-email" },
    };

    [Theory]
    [MemberData(nameof(InvalidReferences))]
    public async Task Add_Invalid_ReturnsInvalidProfileError_AndStoresNothing(
        string _, string name, string relationship, string? email)
    {
        var client = await CleanGuestAsync(factory);

        var payload = await AddAsync(client, name, relationship, email);

        Assert.Equal("InvalidProfileError", ApplicationTests.ErrorType(payload));
        Assert.Empty(await ListAsync(client));
    }

    [Fact]
    public async Task Add_EleventhReference_ReturnsInvalidProfileError()
    {
        var client = await CleanGuestAsync(factory);
        for (var i = 0; i < 10; i++)
            await AddOkAsync(client, $"Ref {i}");

        var payload = await AddAsync(client, "One too many");

        Assert.Equal("InvalidProfileError", ApplicationTests.ErrorType(payload));
        Assert.Equal(10, (await ListAsync(client)).Length);
    }

    [Fact]
    public async Task Update_KeepsIdAndPosition()
    {
        var client = await CleanGuestAsync(factory);
        await AddOkAsync(client, "A");
        var id = await AddOkAsync(client, "B");

        var updated = (await client.GraphQLAsync(UpdateReference, new { id, name = "B2", relationship = "Peer" }))
            .GetProperty("updateProfileReference").GetProperty("profileReference");

        Assert.Equal(id, updated.GetProperty("id").GetInt32());
        Assert.Equal(1, updated.GetProperty("position").GetInt32());
        Assert.Equal("Peer", updated.GetProperty("relationship").GetString());
        Assert.Equal(JsonValueKind.Null, updated.GetProperty("email").ValueKind);
    }

    [Fact]
    public async Task Move_ToTheFront_RenumbersTheRest()
    {
        var client = await CleanGuestAsync(factory);
        await AddOkAsync(client, "First");
        await AddOkAsync(client, "Second");
        var third = await AddOkAsync(client, "Third");

        var moved = (await client.GraphQLAsync(MoveReference, new { id = third, position = 0 }))
            .GetProperty("moveProfileReference").GetProperty("references");

        Assert.Equal(3, moved.GetArrayLength());
        Assert.Equal(["Third", "First", "Second"], await ListAsync(client));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    public async Task Move_OutOfRange_ReturnsInvalidProfileError_AndKeepsOrder(int position)
    {
        var client = await CleanGuestAsync(factory);
        await AddOkAsync(client, "First");
        var second = await AddOkAsync(client, "Second");

        var payload = (await client.GraphQLAsync(MoveReference, new { id = second, position })).GetProperty("moveProfileReference");

        Assert.Equal("InvalidProfileError", ApplicationTests.ErrorType(payload));
        Assert.Equal(["First", "Second"], await ListAsync(client));
    }

    [Fact]
    public async Task Delete_ClosesTheGap()
    {
        var client = await CleanGuestAsync(factory);
        await AddOkAsync(client, "A");
        var middle = await AddOkAsync(client, "B");
        await AddOkAsync(client, "C");

        await client.GraphQLAsync(DeleteReference, new { id = middle });

        Assert.Equal(["A", "C"], await ListAsync(client));
    }

    [Fact]
    public async Task ForeignOrUnknownReference_ReturnsProfileEntryNotFoundError()
    {
        var owner = await factory.GetOwnerClientAsync();
        var guest = await CleanGuestAsync(factory);
        var ownerRef = (await AddAsync(owner, "Owner Ref")).GetProperty("profileReference").GetProperty("id").GetInt32();

        var update = (await guest.GraphQLAsync(UpdateReference, new { id = ownerRef, name = "X", relationship = "Y" }))
            .GetProperty("updateProfileReference");
        var move = (await guest.GraphQLAsync(MoveReference, new { id = ownerRef, position = 0 })).GetProperty("moveProfileReference");
        var delete = (await guest.GraphQLAsync(DeleteReference, new { id = 999999 })).GetProperty("deleteProfileReference");

        Assert.Equal("ProfileEntryNotFoundError", ApplicationTests.ErrorType(update));
        Assert.Equal("ProfileEntryNotFoundError", ApplicationTests.ErrorType(move));
        Assert.Equal("ProfileEntryNotFoundError", ApplicationTests.ErrorType(delete));
        await owner.GraphQLAsync(DeleteReference, new { id = ownerRef });
    }
}
