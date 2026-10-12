using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using JobHunt.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace JobHunt.Api.Tests.Features.Listings;

public class ListingUrlCheckTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Query = """
        query ($urls: [String!]!) {
          checkListingUrls(urls: $urls) {
            index url normalizedUrl existingListingIds duplicateOfIndex shouldProcess
          }
        }
        """;

    [Fact]
    public async Task Check_ReturnsOrderedNewSavedAndRepeatedResults()
    {
        var client = await factory.GetOwnerClientAsync();
        var savedUrl = ListingTests.UniqueUrl();
        var savedId = (await ListingTests.CreateAsync(client,
            ListingTests.NewListing(ListingTests.Unique("Preflight"), url: savedUrl))).GetProperty("id").GetInt32();
        var newUrl = $"https://jobs.example.com/Job/{Guid.NewGuid():N}?id=7";
        string[] urls = [newUrl + "&utm_source=x#apply", savedUrl, newUrl, savedUrl + "/?fbclid=x", newUrl];

        var results = await CheckAsync(client, urls);

        Assert.Equal(urls.Length, results.GetArrayLength());
        for (var i = 0; i < urls.Length; i++)
        {
            Assert.Equal(i, results[i].GetProperty("index").GetInt32());
            Assert.Equal(urls[i], results[i].GetProperty("url").GetString());
        }
        AssertRow(results[0], newUrl, [], null, true);
        AssertRow(results[1], savedUrl, [savedId], null, false);
        AssertRow(results[2], newUrl, [], 0, false);
        AssertRow(results[3], savedUrl, [savedId], 1, false);
        AssertRow(results[4], newUrl, [], 0, false);
    }

    [Theory]
    [InlineData("SAVED")]
    [InlineData("APPLIED")]
    [InlineData("INTERVIEWING")]
    [InlineData("OFFER")]
    [InlineData("REJECTED")]
    [InlineData("WITHDRAWN")]
    [InlineData("ACCEPTED")]
    public async Task Check_IncludesEveryStatusAndArchivedListings(string status)
    {
        var client = await factory.GetOwnerClientAsync();
        var url = ListingTests.UniqueUrl();
        var id = await ListingTests.ListingWithStatusAsync(client, url, status);
        AssertRow((await CheckAsync(client, [url]))[0], url, [id], null, false);

        await client.GraphQLAsync("""
            mutation ($id: Int!) { setListingArchived(input: { id: $id, isArchived: true }) { listing { id } } }
            """, new { id });
        AssertRow((await CheckAsync(client, [url]))[0], url, [id], null, false);
    }

    [Fact]
    public async Task Check_ReturnsAllAttemptsAscendingAndDoesNotBlockReapplication()
    {
        var client = await factory.GetOwnerClientAsync();
        var url = ListingTests.UniqueUrl();
        var ids = new List<int>();
        foreach (var status in new[] { "REJECTED", "WITHDRAWN", "ACCEPTED" })
            ids.Add(await ListingTests.ListingWithStatusAsync(client, url, status));
        AssertRow((await CheckAsync(client, [url]))[0], url, ids.Order().ToArray(), null, false);

        ids.Add((await ListingTests.CreateAsync(client,
            ListingTests.NewListing(ListingTests.Unique("Reapply"), url: url))).GetProperty("id").GetInt32());
        AssertRow((await CheckAsync(client, [url]))[0], url, ids.Order().ToArray(), null, false);
    }

    [Fact]
    public async Task Check_IsolatesOwnerAndGuestMatches()
    {
        var owner = await factory.GetOwnerClientAsync();
        var guest = await factory.GetGuestClientAsync();
        var url = ListingTests.UniqueUrl();
        var ownerId = (await ListingTests.CreateAsync(owner,
            ListingTests.NewListing(ListingTests.Unique("Owner"), url: url))).GetProperty("id").GetInt32();
        AssertRow((await CheckAsync(guest, [url]))[0], url, [], null, true);
        var guestId = (await ListingTests.CreateAsync(guest,
            ListingTests.NewListing(ListingTests.Unique("Guest"), url: url))).GetProperty("id").GetInt32();
        AssertRow((await CheckAsync(guest, [url]))[0], url, [guestId], null, false);
        AssertRow((await CheckAsync(owner, [url]))[0], url, [ownerId], null, false);
    }

    [Fact]
    public async Task Check_RequiresAuthentication()
    {
        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/graphql", new { query = Query, variables = new { urls = new[] { ListingTests.UniqueUrl() } } });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    public async Task Check_AcceptsBoundaryBatchSizes(int count)
    {
        var client = await factory.GetOwnerClientAsync();
        var urls = Enumerable.Range(0, count).Select(_ => ListingTests.UniqueUrl()).ToArray();
        var results = await CheckAsync(client, urls);
        Assert.Equal(count, results.GetArrayLength());
        Assert.All(results.EnumerateArray(), row => Assert.True(row.GetProperty("shouldProcess").GetBoolean()));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task Check_RejectsInvalidBatchSizes(int count)
    {
        var client = await factory.GetOwnerClientAsync();
        var body = await CheckErrorAsync(client, Enumerable.Repeat(ListingTests.UniqueUrl(), count).ToArray());
        Assert.Contains("1 to 100", body.GetProperty("errors")[0].GetProperty("message").GetString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("/jobs/123")]
    [InlineData("javascript:alert(1)")]
    [InlineData("ftp://jobs.example.com/ad")]
    [InlineData("https://")]
    public async Task Check_RejectsInvalidRowWithIndexAndNoPartialResults(string invalid)
    {
        var client = await factory.GetOwnerClientAsync();
        var body = await CheckErrorAsync(client, [ListingTests.UniqueUrl(), invalid, ListingTests.UniqueUrl()]);
        Assert.Contains("index 1", body.GetProperty("errors")[0].GetProperty("message").GetString());
    }

    [Fact]
    public async Task Check_EnforcesTrimmedUrlLength()
    {
        var client = await factory.GetOwnerClientAsync();
        var prefix = "https://jobs.example.com/";
        var url = prefix + new string('a', 2048 - prefix.Length);
        AssertRow((await CheckAsync(client, ["  " + url + "  "]))[0], url, [], null, true);
        var body = await CheckErrorAsync(client, [ListingTests.UniqueUrl(), url + "a"]);
        Assert.Contains("index 1", body.GetProperty("errors")[0].GetProperty("message").GetString());
    }

    [Fact]
    public async Task Check_NormalizesTrackingVariantsButPreservesMeaningfulDifferences()
    {
        var client = await factory.GetOwnerClientAsync();
        var path = $"/Job/{Guid.NewGuid():N}";
        var normalized = $"https://jobs.example.com{path}?id=7&category=IT";
        var original = $"  HTTPS://JOBS.Example.com:443{path}/?id=7&UTM_source=x&FBCLID=y&GCLID=z&category=IT#apply  ";
        var results = await CheckAsync(client, [original, normalized, normalized.Replace("id=7", "id=8"),
            normalized.Replace("/Job/", "/job/"), normalized.Replace("jobs.example.com", "other.example.com"),
            $"https://jobs.example.com{path}?category=IT&id=7"]);
        Assert.Equal(original, results[0].GetProperty("url").GetString());
        AssertRow(results[0], normalized, [], null, true);
        AssertRow(results[1], normalized, [], 0, false);
        for (var i = 2; i < results.GetArrayLength(); i++)
        {
            Assert.True(results[i].GetProperty("shouldProcess").GetBoolean());
            Assert.Equal(JsonValueKind.Null, results[i].GetProperty("duplicateOfIndex").ValueKind);
        }
    }

    [Fact]
    public async Task Check_IsReadOnlyAndOnlySavedUrlsAffectLaterRequests()
    {
        var client = await factory.GetOwnerClientAsync();
        var savedUrl = ListingTests.UniqueUrl();
        await ListingTests.CreateAsync(client, ListingTests.NewListing(ListingTests.Unique("Read only"), url: savedUrl));
        var url = ListingTests.UniqueUrl();
        var before = await CountsAsync();
        var pageRequests = factory.Pages.Requests;
        var aiCalls = 0;
        factory.Llm.Respond = () => { aiCalls++; throw new InvalidOperationException("Preflight must not invoke AI."); };

        AssertRow((await CheckAsync(client, [url, savedUrl]))[0], url, [], null, true);
        AssertRow((await CheckAsync(client, [url]))[0], url, [], null, true);
        await CheckErrorAsync(client, [url, "invalid"]);
        Assert.Equal(before, await CountsAsync());
        Assert.Equal(pageRequests, factory.Pages.Requests);
        Assert.Equal(0, aiCalls);
        Assert.Empty(factory.Llm.LastMessages);

        var id = (await ListingTests.CreateAsync(client,
            ListingTests.NewListing(ListingTests.Unique("Saved later"), url: url))).GetProperty("id").GetInt32();
        AssertRow((await CheckAsync(client, [url]))[0], url, [id], null, false);
        // A prior recommendation cannot bypass create-time duplicate enforcement.
        var data = await client.GraphQLAsync("""
            mutation ($listing: ListingInput!) {
              createListing(input: { listing: $listing }) {
                errors { __typename ... on DuplicateListingError { existingListingId } }
              }
            }
            """, new { listing = ListingTests.NewListing(ListingTests.Unique("Concurrent save"), url: url) });
        var error = Assert.Single(data.GetProperty("createListing").GetProperty("errors").EnumerateArray());
        Assert.Equal("DuplicateListingError", error.GetProperty("__typename").GetString());
        Assert.Equal(id, error.GetProperty("existingListingId").GetInt32());
    }

    private async Task<(int Listings, int ParseJobs, int Usage)> CountsAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return (await db.Listings.IgnoreQueryFilters().CountAsync(),
            await db.ParseJobs.IgnoreQueryFilters().CountAsync(), await db.LlmUsages.IgnoreQueryFilters().CountAsync());
    }

    private static async Task<JsonElement> CheckAsync(HttpClient client, string[] urls) =>
        (await client.GraphQLAsync(Query, new { urls })).GetProperty("checkListingUrls");

    private static async Task<JsonElement> CheckErrorAsync(HttpClient client, string[] urls)
    {
        var response = await client.PostAsJsonAsync("/graphql", new { query = Query, variables = new { urls } });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.NotEmpty(body.GetProperty("errors").EnumerateArray());
        Assert.True(!body.TryGetProperty("data", out var data) || data.ValueKind == JsonValueKind.Null
            || data.GetProperty("checkListingUrls").ValueKind == JsonValueKind.Null);
        return body;
    }

    private static void AssertRow(JsonElement row, string normalizedUrl, int[] ids, int? duplicateOfIndex, bool shouldProcess)
    {
        Assert.Equal(normalizedUrl, row.GetProperty("normalizedUrl").GetString());
        Assert.Equal(ids, row.GetProperty("existingListingIds").EnumerateArray().Select(x => x.GetInt32()).ToArray());
        var duplicate = row.GetProperty("duplicateOfIndex");
        if (duplicateOfIndex is { } index) Assert.Equal(index, duplicate.GetInt32());
        else Assert.Equal(JsonValueKind.Null, duplicate.ValueKind);
        Assert.Equal(shouldProcess, row.GetProperty("shouldProcess").GetBoolean());
    }
}
