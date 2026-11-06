using System.Text.Json;
using JobHunt.Api.Features.ListingImports;
using Microsoft.Extensions.DependencyInjection;

namespace JobHunt.Api.Tests.Features.ListingImports;

public class ListingImportTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string ValidJson = """
        [
          { "title": "Sr Front End Engineer", "companyName": "Angel", "url": "https://example.com/jobs/1" },
          { "title": "Godot Developer", "companyName": "Xperteez", "url": "https://example.com/jobs/2" }
        ]
        """;

    private const string List = """
        query ($isReviewed: Boolean) {
          listingImports(isReviewed: $isReviewed, first: 50) {
            nodes { id fileName kind generatedAt itemCount content error reviewedAt }
          }
        }
        """;

    private const string SetReviewed = """
        mutation ($id: Int!, $isReviewed: Boolean!) {
          setListingImportReviewed(input: { id: $id, isReviewed: $isReviewed }) {
            listingImport { id reviewedAt }
            errors { __typename }
          }
        }
        """;

    [Fact]
    public async Task NewFile_IsStoredAsPendingWithKindDateAndCount_AndNotImportedTwice()
    {
        var client = await factory.GetOwnerClientAsync();
        var before = await PendingCountAsync(client);
        Drop(ApiFactory.OwnerEmail, "2026-11-02_16-01-22-remote-it.json", ValidJson);

        await ScanAsync();
        await ScanAsync();

        var import = Single(await ImportsAsync(client), "2026-11-02_16-01-22-remote-it.json");
        Assert.Equal("remote-it", import.GetProperty("kind").GetString());
        Assert.Equal(2, import.GetProperty("itemCount").GetInt32());
        Assert.Equal(ValidJson, import.GetProperty("content").GetString());
        Assert.Equal(JsonValueKind.Null, import.GetProperty("error").ValueKind);
        var expected = TimeZoneInfo.ConvertTimeToUtc(new DateTime(2026, 11, 2, 16, 1, 22), TimeZoneInfo.Local);
        Assert.Equal(expected, import.GetProperty("generatedAt").GetDateTime().ToUniversalTime());
        Assert.Equal(before + 1, await PendingCountAsync(client));
    }

    [Fact]
    public async Task DateOnlyName_IsAccepted()
    {
        var client = await factory.GetOwnerClientAsync();
        Drop(ApiFactory.OwnerEmail, "2026-11-01-agencies.json", ValidJson);

        await ScanAsync();

        Assert.Equal("agencies", Single(await ImportsAsync(client), "2026-11-01-agencies.json").GetProperty("kind").GetString());
    }

    [Theory]
    [InlineData("2026-11-03_10-00-00-broken.json", "not json", "File isn't valid JSON.")]
    [InlineData("2026-11-03_10-00-01-object.json", """{ "url": "https://example.com" }""", "File must be a non-empty JSON array of listings.")]
    [InlineData("2026-11-03_10-00-02-nourl.json", """[{ "url": "https://example.com/a" }, { "title": "x" }]""", "Listing 2 needs an http(s) url.")]
    [InlineData("2026-11-03_10-00-03-ftp.json", """[{ "url": "ftp://example.com/a" }]""", "Listing 1 needs an http(s) url.")]
    public async Task InvalidFile_IsStoredWithAnErrorAndNoContent(string fileName, string json, string error)
    {
        var client = await factory.GetOwnerClientAsync();
        Drop(ApiFactory.OwnerEmail, fileName, json);

        await ScanAsync();

        var import = Single(await ImportsAsync(client), fileName);
        Assert.Equal(error, import.GetProperty("error").GetString());
        Assert.Equal(JsonValueKind.Null, import.GetProperty("content").ValueKind);
    }

    [Fact]
    public async Task UnrecognizedNames_UnknownEmailFolders_AndFilesStillBeingWritten_AreSkipped()
    {
        var client = await factory.GetOwnerClientAsync();
        Drop(ApiFactory.OwnerEmail, "notes.json", ValidJson);
        Drop(ApiFactory.OwnerEmail, "2026-13-40-it.json", ValidJson);
        Drop("nobody@test.local", "2026-11-03_11-00-00-it.json", ValidJson);
        Drop(ApiFactory.OwnerEmail, "2026-11-03_12-00-00-fresh.json", ValidJson, settled: false);

        await ScanAsync();

        var names = (await ImportsAsync(client)).Select(i => i.GetProperty("fileName").GetString()).ToList();
        Assert.DoesNotContain("notes.json", names);
        Assert.DoesNotContain("2026-13-40-it.json", names);
        Assert.DoesNotContain("2026-11-03_11-00-00-it.json", names);
        Assert.DoesNotContain("2026-11-03_12-00-00-fresh.json", names);
    }

    [Fact]
    public async Task EachEmailFolder_ImportsForThatAccountOnly()
    {
        var owner = await factory.GetOwnerClientAsync();
        var second = await factory.GetSecondOwnerClientAsync();
        Drop(ApiFactory.OwnerEmail, "2026-11-03_13-00-00-it.json", ValidJson);
        // Folder names match the account email case-insensitively.
        Drop(ApiFactory.SecondOwnerEmail.ToUpperInvariant(), "2026-11-03_13-00-01-generic.json", ValidJson);

        await ScanAsync();

        var ownerNames = (await ImportsAsync(owner)).Select(i => i.GetProperty("fileName").GetString()).ToList();
        var secondNames = (await ImportsAsync(second)).Select(i => i.GetProperty("fileName").GetString()).ToList();
        Assert.Contains("2026-11-03_13-00-00-it.json", ownerNames);
        Assert.DoesNotContain("2026-11-03_13-00-01-generic.json", ownerNames);
        Assert.Contains("2026-11-03_13-00-01-generic.json", secondNames);
        Assert.DoesNotContain("2026-11-03_13-00-00-it.json", secondNames);
    }

    [Fact]
    public async Task SetReviewed_MovesItOutOfPending_AndBack()
    {
        var client = await factory.GetOwnerClientAsync();
        Drop(ApiFactory.OwnerEmail, "2026-11-03_14-00-00-it.json", ValidJson);
        await ScanAsync();
        var id = Single(await ImportsAsync(client), "2026-11-03_14-00-00-it.json").GetProperty("id").GetInt32();
        var pending = await PendingCountAsync(client);

        var reviewed = (await client.GraphQLAsync(SetReviewed, new { id, isReviewed = true }))
            .GetProperty("setListingImportReviewed").GetProperty("listingImport");

        Assert.NotEqual(JsonValueKind.Null, reviewed.GetProperty("reviewedAt").ValueKind);
        Assert.Equal(pending - 1, await PendingCountAsync(client));
        Assert.DoesNotContain(await ImportsAsync(client, isReviewed: false), i => i.GetProperty("id").GetInt32() == id);
        Assert.Contains(await ImportsAsync(client, isReviewed: true), i => i.GetProperty("id").GetInt32() == id);

        await client.GraphQLAsync(SetReviewed, new { id, isReviewed = false });

        Assert.Equal(pending, await PendingCountAsync(client));
    }

    [Fact]
    public async Task SetReviewed_OnAnotherUsersImport_IsNotFound()
    {
        var owner = await factory.GetOwnerClientAsync();
        var second = await factory.GetSecondOwnerClientAsync();
        Drop(ApiFactory.OwnerEmail, "2026-11-03_15-00-00-it.json", ValidJson);
        await ScanAsync();
        var id = Single(await ImportsAsync(owner), "2026-11-03_15-00-00-it.json").GetProperty("id").GetInt32();

        var result = (await second.GraphQLAsync(SetReviewed, new { id, isReviewed = true }))
            .GetProperty("setListingImportReviewed");

        Assert.Equal("ListingImportNotFoundError", result.GetProperty("errors")[0].GetProperty("__typename").GetString());
        Assert.Equal(JsonValueKind.Null, Single(await ImportsAsync(owner), "2026-11-03_15-00-00-it.json")
            .GetProperty("reviewedAt").ValueKind);
    }

    private void Drop(string email, string fileName, string json, bool settled = true)
    {
        var folder = Directory.CreateDirectory(System.IO.Path.Combine(factory.ListingImportFolder, email));
        var path = System.IO.Path.Combine(folder.FullName, fileName);
        File.WriteAllText(path, json);
        // The scanner waits a few seconds after the last write; backdate so it doesn't have to.
        if (settled)
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(-1));
    }

    private Task ScanAsync() => factory.Services.GetRequiredService<ListingImportScanner>().ScanOnceAsync();

    private static async Task<List<JsonElement>> ImportsAsync(HttpClient client, bool? isReviewed = null) =>
        (await client.GraphQLAsync(List, new { isReviewed }))
            .GetProperty("listingImports").GetProperty("nodes").EnumerateArray().ToList();

    private static JsonElement Single(List<JsonElement> imports, string fileName) =>
        Assert.Single(imports, i => i.GetProperty("fileName").GetString() == fileName);

    private static async Task<int> PendingCountAsync(HttpClient client) =>
        (await client.GraphQLAsync("{ pendingListingImportCount }")).GetProperty("pendingListingImportCount").GetInt32();
}
