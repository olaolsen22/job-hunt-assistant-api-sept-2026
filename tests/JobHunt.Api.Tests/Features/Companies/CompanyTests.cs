using System.Text.Json;
using JobHunt.Api.Tests.Features.Listings;

namespace JobHunt.Api.Tests.Features.Companies;

public class CompanyTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string UpdateCompany = """
        mutation ($id: Int!, $name: String!, $industryName: String, $locations: [LocationInput!]!) {
          updateCompany(input: { id: $id, name: $name, industryName: $industryName, locations: $locations }) {
            company { id name industry { name } locations { location { city countryCode } } }
            errors { __typename }
          }
        }
        """;

    [Fact]
    public async Task UpdateCompany_ReplacesIndustryAndLocations()
    {
        var client = await factory.GetOwnerClientAsync();
        var name = ListingTests.Unique("Acme");
        var oslo = ListingTests.Unique("Oslo");
        var bergen = ListingTests.Unique("Bergen");
        var id = await CompanyIdAsync(client, name);

        await client.GraphQLAsync(UpdateCompany, new
        {
            id, name, industryName = "Energy", locations = new[] { new { city = oslo, countryCode = "NO" } },
        });
        var data = await client.GraphQLAsync(UpdateCompany, new
        {
            id, name, industryName = "energy",
            locations = new[] { new { city = oslo, countryCode = "NO" }, new { city = bergen, countryCode = "NO" } },
        });
        var company = data.GetProperty("updateCompany").GetProperty("company");

        Assert.Equal("Energy", company.GetProperty("industry").GetProperty("name").GetString());
        Assert.Equal([bergen, oslo], Cities(company).Order());

        var replaced = await client.GraphQLAsync(UpdateCompany, new
        {
            id, name, industryName = "Energy", locations = new[] { new { city = bergen, countryCode = "NO" } },
        });
        Assert.Equal([bergen], Cities(replaced.GetProperty("updateCompany").GetProperty("company")));
    }

    [Fact]
    public async Task UpdateCompany_WithoutIndustryOrLocations_ClearsThem()
    {
        var client = await factory.GetOwnerClientAsync();
        var name = ListingTests.Unique("Acme");
        var id = await CompanyIdAsync(client, name);
        await client.GraphQLAsync(UpdateCompany, new
        {
            id, name, industryName = "Energy", locations = new[] { new { city = "Oslo", countryCode = "NO" } },
        });

        var data = await client.GraphQLAsync(UpdateCompany, new
        {
            id, name, industryName = (string?)null, locations = Array.Empty<object>(),
        });
        var company = data.GetProperty("updateCompany").GetProperty("company");

        Assert.Equal(JsonValueKind.Null, company.GetProperty("industry").ValueKind);
        Assert.Empty(company.GetProperty("locations").EnumerateArray());
    }

    [Fact]
    public async Task UpdateCompany_RenamedOntoExistingName_FailsAndLeavesCompanyUnchanged()
    {
        var client = await factory.GetOwnerClientAsync();
        var taken = ListingTests.Unique("Equinor");
        var name = ListingTests.Unique("Aker");
        await CompanyIdAsync(client, taken);
        var id = await CompanyIdAsync(client, name);

        var data = await client.GraphQLAsync(UpdateCompany, new
        {
            id, name = taken.ToLowerInvariant(), industryName = "Energy", locations = Array.Empty<object>(),
        });

        Assert.Equal("InvalidCompanyError",
            data.GetProperty("updateCompany").GetProperty("errors")[0].GetProperty("__typename").GetString());
        var after = await client.GraphQLAsync("""
            query ($id: Int!) { companies(where: { id: { eq: $id } }) { nodes { name industry { name } } } }
            """, new { id });
        var node = after.GetProperty("companies").GetProperty("nodes")[0];
        Assert.Equal(name, node.GetProperty("name").GetString());
    }

    [Fact]
    public async Task UpdateCompany_WithUnknownId_ReturnsCompanyNotFoundError()
    {
        var client = await factory.GetOwnerClientAsync();

        var data = await client.GraphQLAsync(UpdateCompany, new
        {
            id = int.MaxValue, name = "Nope", industryName = (string?)null, locations = Array.Empty<object>(),
        });

        Assert.Equal("CompanyNotFoundError",
            data.GetProperty("updateCompany").GetProperty("errors")[0].GetProperty("__typename").GetString());
    }

    [Fact]
    public async Task Companies_CanBeFilteredByIndustry()
    {
        var client = await factory.GetOwnerClientAsync();
        var industry = ListingTests.Unique("Shipping");
        var inIndustry = ListingTests.Unique("Acme");
        var other = ListingTests.Unique("Other");
        var id = await CompanyIdAsync(client, inIndustry);
        await CompanyIdAsync(client, other);
        await client.GraphQLAsync(UpdateCompany, new
        {
            id, name = inIndustry, industryName = industry, locations = Array.Empty<object>(),
        });

        var data = await client.GraphQLAsync("""
            query ($industry: String!) {
              companies(where: { industry: { name: { eq: $industry } } }) { nodes { name } }
            }
            """, new { industry });

        var names = data.GetProperty("companies").GetProperty("nodes").EnumerateArray()
            .Select(n => n.GetProperty("name").GetString()).ToList();
        Assert.Equal([inIndustry], names);
    }

    [Fact]
    public async Task Industries_SearchIsCaseInsensitiveOrderedAndPerUser()
    {
        var owner = await factory.GetOwnerClientAsync();
        var guest = await factory.GetGuestClientAsync();
        var marker = Guid.NewGuid().ToString("N")[..8];
        foreach (var industry in new[] { $"Zeta {marker}", $"Alpha {marker}" })
        {
            var name = ListingTests.Unique("Acme");
            await owner.GraphQLAsync(UpdateCompany, new
            {
                id = await CompanyIdAsync(owner, name), name, industryName = industry, locations = Array.Empty<object>(),
            });
        }

        const string Query = "query ($s: String) { industries(search: $s) { nodes { name } } }";
        var found = await owner.GraphQLAsync(Query, new { s = marker.ToUpperInvariant() });
        var guestFound = await guest.GraphQLAsync(Query, new { s = marker });

        Assert.Equal([$"Alpha {marker}", $"Zeta {marker}"],
            found.GetProperty("industries").GetProperty("nodes").EnumerateArray().Select(n => n.GetProperty("name").GetString()));
        Assert.Empty(guestFound.GetProperty("industries").GetProperty("nodes").EnumerateArray());
    }

    private const string CreateCompanies = """
        mutation ($companies: [CompanyInput!]!) {
          createCompanies(input: { companies: $companies }) {
            results { created company { id name industry { name } locations { location { id city countryCode } } } }
            errors { __typename }
          }
        }
        """;

    private static object Row(string name, string? industry = null, params object[] locations) =>
        new { name, industryName = industry, locations };

    private static object Place(string city, string countryCode = "NO") => new { city, countryCode };

    [Fact]
    public async Task CreateCompanies_CreatesNewCompaniesInOrder()
    {
        var client = await factory.GetOwnerClientAsync();
        var (first, second) = (ListingTests.Unique("Acme"), ListingTests.Unique("Beta"));

        var data = await client.GraphQLAsync(CreateCompanies, new
        {
            companies = new[] { Row(first, "Energy", Place(ListingTests.Unique("Oslo"))), Row(second) },
        });
        var results = data.GetProperty("createCompanies").GetProperty("results").EnumerateArray().ToList();

        Assert.Equal([first, second], results.Select(r => r.GetProperty("company").GetProperty("name").GetString()));
        Assert.All(results, r => Assert.True(r.GetProperty("created").GetBoolean()));
        Assert.Equal("Energy", results[0].GetProperty("company").GetProperty("industry").GetProperty("name").GetString());
        Assert.Single(results[0].GetProperty("company").GetProperty("locations").EnumerateArray());
        Assert.Empty(results[1].GetProperty("company").GetProperty("locations").EnumerateArray());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task CreateCompanies_WithEmptyOrOversizedBatch_ReturnsInvalidCompanyError(int count)
    {
        var client = await factory.GetOwnerClientAsync();
        var marker = Guid.NewGuid().ToString("N");

        var data = await client.GraphQLAsync(CreateCompanies, new
        {
            companies = Enumerable.Range(0, count).Select(i => Row($"{marker} {i}")).ToArray(),
        });

        Assert.Equal("InvalidCompanyError",
            data.GetProperty("createCompanies").GetProperty("errors")[0].GetProperty("__typename").GetString());
        Assert.Equal(0, await CountAsync(client, marker));
    }

    [Fact]
    public async Task CreateCompanies_WithExistingName_ReturnsItUnchanged()
    {
        var client = await factory.GetOwnerClientAsync();
        var name = ListingTests.Unique("Ørsted");
        var created = await client.GraphQLAsync(CreateCompanies, new { companies = new[] { Row(name, "Energy") } });
        var id = Single(created).GetProperty("company").GetProperty("id").GetInt32();

        var again = await client.GraphQLAsync(CreateCompanies, new
        {
            companies = new[] { Row($" {name.ToUpperInvariant()} ", "Shipping", Place("Bergen")) },
        });
        var result = Single(again);

        Assert.False(result.GetProperty("created").GetBoolean());
        Assert.Equal(id, result.GetProperty("company").GetProperty("id").GetInt32());
        Assert.Equal("Energy", result.GetProperty("company").GetProperty("industry").GetProperty("name").GetString());
        Assert.Empty(result.GetProperty("company").GetProperty("locations").EnumerateArray());
        Assert.Equal(1, await CountAsync(client, name));
    }

    [Fact]
    public async Task CreateCompanies_WithRepeatInBatch_KeepsTheFirstRow()
    {
        var client = await factory.GetOwnerClientAsync();
        var name = ListingTests.Unique("Aker");

        var data = await client.GraphQLAsync(CreateCompanies, new
        {
            companies = new[] { Row(name, "Energy"), Row(ListingTests.Unique("Other")), Row(name.ToLowerInvariant(), "Shipping") },
        });
        var results = data.GetProperty("createCompanies").GetProperty("results").EnumerateArray().ToList();

        Assert.Equal([true, true, false], results.Select(r => r.GetProperty("created").GetBoolean()));
        Assert.Equal(results[0].GetProperty("company").GetProperty("id").GetInt32(),
            results[2].GetProperty("company").GetProperty("id").GetInt32());
        Assert.Equal("Energy", results[2].GetProperty("company").GetProperty("industry").GetProperty("name").GetString());
    }

    [Fact]
    public async Task CreateCompanies_ShareNewIndustriesAndLocationsInOneBatch()
    {
        var client = await factory.GetOwnerClientAsync();
        var industry = ListingTests.Unique("Shipping");
        var city = ListingTests.Unique("Stavanger");

        var data = await client.GraphQLAsync(CreateCompanies, new
        {
            companies = new[]
            {
                Row(ListingTests.Unique("A"), industry, Place(city), Place(city.ToUpperInvariant())),
                Row(ListingTests.Unique("B"), industry.ToLowerInvariant(), Place(city)),
            },
        });
        var results = data.GetProperty("createCompanies").GetProperty("results").EnumerateArray().ToList();

        Assert.Equal(2, results.Count);
        var locations = results.Select(r => r.GetProperty("company").GetProperty("locations")).ToList();
        Assert.Single(locations[0].EnumerateArray());
        Assert.Equal(locations[0][0].GetProperty("location").GetProperty("id").GetInt32(),
            locations[1][0].GetProperty("location").GetProperty("id").GetInt32());
        var industries = await client.GraphQLAsync(
            "query ($s: String) { industries(search: $s) { totalCount } }", new { s = industry });
        Assert.Equal(1, industries.GetProperty("industries").GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task CreateCompanies_WithBlankNameOrBadCountry_SavesNothing()
    {
        var client = await factory.GetOwnerClientAsync();
        var marker = Guid.NewGuid().ToString("N");
        var industry = $"Industry {marker}";
        var city = $"City {marker}";

        var blank = await client.GraphQLAsync(CreateCompanies, new
        {
            companies = new[] { Row($"{marker} 1", industry, Place(city)), Row($"{marker} 2"), Row("  ") },
        });
        var badCountry = await client.GraphQLAsync(CreateCompanies, new
        {
            companies = new[] { Row($"{marker} 3", industry, Place(city)), Row($"{marker} 4", null, Place(city, "XX")) },
        });

        Assert.Equal("InvalidCompanyError",
            blank.GetProperty("createCompanies").GetProperty("errors")[0].GetProperty("__typename").GetString());
        Assert.Equal("InvalidLocationError",
            badCountry.GetProperty("createCompanies").GetProperty("errors")[0].GetProperty("__typename").GetString());
        Assert.Equal(0, await CountAsync(client, marker));
        var rest = await client.GraphQLAsync("""
            query ($s: String) {
              industries(search: $s) { totalCount }
              locations(search: $s) { totalCount }
            }
            """, new { s = marker });
        Assert.Equal(0, rest.GetProperty("industries").GetProperty("totalCount").GetInt32());
        Assert.Equal(0, rest.GetProperty("locations").GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task CreateCompanies_WithBadCountryOnExistingName_StillFailsTheBatch()
    {
        var client = await factory.GetOwnerClientAsync();
        var name = ListingTests.Unique("Acme");
        await client.GraphQLAsync(CreateCompanies, new { companies = new[] { Row(name) } });

        var data = await client.GraphQLAsync(CreateCompanies, new
        {
            companies = new[] { Row(name, null, Place("Oslo", "XX")) },
        });

        Assert.Equal("InvalidLocationError",
            data.GetProperty("createCompanies").GetProperty("errors")[0].GetProperty("__typename").GetString());
    }

    [Fact]
    public async Task CreateCompanies_WithRepeatedLocationInRow_CollapsesIt()
    {
        var client = await factory.GetOwnerClientAsync();
        var city = ListingTests.Unique("Oslo");

        var data = await client.GraphQLAsync(CreateCompanies, new
        {
            companies = new[] { Row(ListingTests.Unique("Acme"), null, Place(city), Place(city.ToLowerInvariant())) },
        });

        Assert.Single(Single(data).GetProperty("company").GetProperty("locations").EnumerateArray());
    }

    [Fact]
    public async Task CreateCompanies_DoesNotMatchAnotherUsersCompany()
    {
        var owner = await factory.GetOwnerClientAsync();
        var guest = await factory.GetGuestClientAsync();
        var name = ListingTests.Unique("Equinor");
        var ownerResult = Single(await owner.GraphQLAsync(CreateCompanies, new { companies = new[] { Row(name) } }));

        var guestResult = Single(await guest.GraphQLAsync(CreateCompanies, new { companies = new[] { Row(name) } }));

        Assert.True(guestResult.GetProperty("created").GetBoolean());
        Assert.NotEqual(ownerResult.GetProperty("company").GetProperty("id").GetInt32(),
            guestResult.GetProperty("company").GetProperty("id").GetInt32());
    }

    private static JsonElement Single(JsonElement data) =>
        data.GetProperty("createCompanies").GetProperty("results").EnumerateArray().Single();

    private static async Task<int> CountAsync(HttpClient client, string search)
    {
        var data = await client.GraphQLAsync(
            "query ($s: String) { companies(search: $s) { totalCount } }", new { s = search });
        return data.GetProperty("companies").GetProperty("totalCount").GetInt32();
    }

    // Quickest way to get a company with a known id.
    private const string SetFavorite = """
        mutation ($id: Int!, $isFavorite: Boolean!) {
          setCompanyFavorite(input: { id: $id, isFavorite: $isFavorite }) {
            company { id favoritedAt }
            errors { __typename }
          }
        }
        """;

    [Fact]
    public async Task SetCompanyFavorite_SetsClearsAndKeepsFirstTime()
    {
        var client = await factory.GetOwnerClientAsync();
        var id = await CompanyIdAsync(client, ListingTests.Unique("Acme"));

        var first = await FavoriteAsync(client, id, true);
        var again = await FavoriteAsync(client, id, true);
        var removed = await FavoriteAsync(client, id, false);

        Assert.NotEqual(JsonValueKind.Null, first.GetProperty("favoritedAt").ValueKind);
        Assert.Equal(first.GetProperty("favoritedAt").GetString(), again.GetProperty("favoritedAt").GetString());
        Assert.Equal(JsonValueKind.Null, removed.GetProperty("favoritedAt").ValueKind);
    }

    [Fact]
    public async Task SetCompanyFavorite_OnUnknownOrOtherUsersCompany_ReturnsCompanyNotFoundError()
    {
        var owner = await factory.GetOwnerClientAsync();
        var guest = await factory.GetGuestClientAsync();
        var id = await CompanyIdAsync(owner, ListingTests.Unique("Acme"));

        foreach (var data in new[]
                 {
                     await guest.GraphQLAsync(SetFavorite, new { id, isFavorite = true }),
                     await owner.GraphQLAsync(SetFavorite, new { id = int.MaxValue, isFavorite = true }),
                 })
            Assert.Equal("CompanyNotFoundError",
                data.GetProperty("setCompanyFavorite").GetProperty("errors")[0].GetProperty("__typename").GetString());
    }

    [Fact]
    public async Task Companies_FavoritesAreNewestFirstAndExcludeOthers()
    {
        var client = await factory.GetOwnerClientAsync();
        var a = await CompanyIdAsync(client, ListingTests.Unique("Acme"));
        var b = await CompanyIdAsync(client, ListingTests.Unique("Bravo"));
        await CompanyIdAsync(client, ListingTests.Unique("Cargo"));
        await FavoriteAsync(client, a, true);
        await FavoriteAsync(client, b, true);

        var data = await client.GraphQLAsync("""
            { companies(where: { favoritedAt: { neq: null } }, order: [{ favoritedAt: DESC }]) { nodes { id } } }
            """);
        var ids = data.GetProperty("companies").GetProperty("nodes").EnumerateArray()
            .Select(n => n.GetProperty("id").GetInt32()).ToList();

        // Other tests share this user, so check only the relative order of ours.
        Assert.Equal([b, a], ids.Where(i => i == a || i == b));
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public async Task Favorite_SurvivesUpdateCompanyAndCreateCompanies()
    {
        var client = await factory.GetOwnerClientAsync();
        var name = ListingTests.Unique("Acme");
        var id = await CompanyIdAsync(client, name);
        var favorited = (await FavoriteAsync(client, id, true)).GetProperty("favoritedAt").GetString();

        await client.GraphQLAsync(UpdateCompany, new
        {
            id, name = name + " AS", industryName = (string?)null, locations = Array.Empty<object>(),
        });
        await client.GraphQLAsync("""
            mutation ($name: String!) {
              createCompanies(input: { companies: [{ name: $name, locations: [] }] }) { errors { __typename } }
            }
            """, new { name = name + " AS" });

        var after = await client.GraphQLAsync("""
            query ($id: Int!) { companies(where: { id: { eq: $id } }) { nodes { favoritedAt } } }
            """, new { id });
        Assert.Equal(favorited, after.GetProperty("companies").GetProperty("nodes")[0].GetProperty("favoritedAt").GetString());
    }

    private static async Task<JsonElement> FavoriteAsync(HttpClient client, int id, bool isFavorite) =>
        (await client.GraphQLAsync(SetFavorite, new { id, isFavorite }))
        .GetProperty("setCompanyFavorite").GetProperty("company");

    private static async Task<int> CompanyIdAsync(HttpClient client, string name)
    {
        var listing = await ListingTests.CreateAsync(client, ListingTests.NewListing(name));
        return listing.GetProperty("company").GetProperty("id").GetInt32();
    }

    private static string[] Cities(JsonElement company) =>
        company.GetProperty("locations").EnumerateArray()
            .Select(l => l.GetProperty("location").GetProperty("city").GetString()!).ToArray();
}
