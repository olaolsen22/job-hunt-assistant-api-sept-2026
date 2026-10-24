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
    public async Task CreateCompanies_WithExistingName_FillsOnlyEmptyFields()
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
        Assert.Equal(["Bergen"], Cities(result.GetProperty("company")));
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

    private const string CreateResearched = """
        mutation ($companies: [CompanyInput!]!) {
          createCompanies(input: { companies: $companies }) {
            results {
              created filled
              company {
                id name notes favoritedAt orgNumber websiteUrl careersUrl linkedInUrl kind sizeBand foundedYear
                description workMode workingLanguage researchedAt industry { name } skills { skill { id name } }
              }
            }
            errors { __typename }
          }
        }
        """;

    private const string UpdateDetails = """
        mutation ($id: Int!, $details: CompanyDetailsInput!) {
          updateCompanyDetails(input: { id: $id, details: $details }) {
            company { orgNumber websiteUrl kind foundedYear description skills { skill { name } } }
            errors { __typename }
          }
        }
        """;

    // A random org.nr that passes the mod-11 check, so tests sharing the DB never collide.
    internal static string OrgNumber()
    {
        int[] weights = [3, 2, 7, 6, 5, 4, 3, 2];
        while (true)
        {
            var digits = Random.Shared.Next(80_000_000, 99_999_999).ToString();
            var remainder = weights.Select((w, i) => w * (digits[i] - '0')).Sum() % 11;
            var check = remainder == 0 ? 0 : 11 - remainder;
            if (check != 10) return digits + check;
        }
    }

    private static object Researched(string name, string? orgNumber = null, string[]? skills = null,
        string? websiteUrl = "https://acme.example", int? foundedYear = 2010) => new
    {
        name, industryName = (string?)null, locations = Array.Empty<object>(), orgNumber, websiteUrl,
        careersUrl = "https://acme.example/jobs", linkedInUrl = (string?)null, kind = "CONSULTANCY",
        sizeBand = "MEDIUM", foundedYear, description = " Builds things. ", workMode = "HYBRID",
        workingLanguage = "NORWEGIAN", skills = skills ?? [],
    };

    private static List<JsonElement> Results(JsonElement data) =>
        data.GetProperty("createCompanies").GetProperty("results").EnumerateArray().ToList();

    private static string[] SkillNames(JsonElement company) =>
        company.GetProperty("skills").EnumerateArray()
            .Select(s => s.GetProperty("skill").GetProperty("name").GetString()!).Order().ToArray();

    [Fact]
    public async Task CreateCompanies_WithResearch_SavesAllFields()
    {
        var client = await factory.GetOwnerClientAsync();
        var name = ListingTests.Unique("Acme");
        var org = OrgNumber();
        var skill = ListingTests.Unique("Kotlin");

        var data = await client.GraphQLAsync(CreateResearched, new
        {
            companies = new[] { Researched(name, $"{org[..3]} {org[3..6]} {org[6..]}", [skill, skill.ToLowerInvariant(), " "]) },
        });
        var result = Results(data).Single();
        var company = result.GetProperty("company");

        Assert.True(result.GetProperty("created").GetBoolean());
        Assert.Equal(org, company.GetProperty("orgNumber").GetString());
        Assert.Equal("https://acme.example", company.GetProperty("websiteUrl").GetString());
        Assert.Equal("CONSULTANCY", company.GetProperty("kind").GetString());
        Assert.Equal("MEDIUM", company.GetProperty("sizeBand").GetString());
        Assert.Equal(2010, company.GetProperty("foundedYear").GetInt32());
        Assert.Equal("Builds things.", company.GetProperty("description").GetString());
        Assert.Equal("HYBRID", company.GetProperty("workMode").GetString());
        Assert.Equal("NORWEGIAN", company.GetProperty("workingLanguage").GetString());
        Assert.NotEqual(JsonValueKind.Null, company.GetProperty("researchedAt").ValueKind);
        Assert.Equal([skill], SkillNames(company));
    }

    [Fact]
    public async Task CreateCompanies_OnExistingCompany_FillsOnlyBlanks()
    {
        var client = await factory.GetOwnerClientAsync();
        var name = ListingTests.Unique("Acme");
        var id = await CompanyIdAsync(client, name);
        await NotesAsync(client, id, "Mine");
        await FavoriteAsync(client, id, true);
        await client.GraphQLAsync(UpdateDetails, new
        {
            id, details = new { websiteUrl = "https://mine.example", foundedYear = 1999, skills = Array.Empty<string>() },
        });
        var skill = ListingTests.Unique("Go");

        var data = await client.GraphQLAsync(CreateResearched, new
        {
            companies = new[] { Researched(name.ToUpperInvariant(), OrgNumber(), [skill]) },
        });
        var result = Results(data).Single();
        var company = result.GetProperty("company");

        Assert.False(result.GetProperty("created").GetBoolean());
        Assert.True(result.GetProperty("filled").GetBoolean());
        Assert.Equal(id, company.GetProperty("id").GetInt32());
        Assert.Equal(name, company.GetProperty("name").GetString());
        Assert.Equal("Mine", company.GetProperty("notes").GetString());
        Assert.NotEqual(JsonValueKind.Null, company.GetProperty("favoritedAt").ValueKind);
        Assert.Equal("https://mine.example", company.GetProperty("websiteUrl").GetString());
        Assert.Equal(1999, company.GetProperty("foundedYear").GetInt32());
        Assert.Equal("https://acme.example/jobs", company.GetProperty("careersUrl").GetString());
        Assert.Equal("CONSULTANCY", company.GetProperty("kind").GetString());
        Assert.Equal([skill], SkillNames(company));
        Assert.NotEqual(JsonValueKind.Null, company.GetProperty("researchedAt").ValueKind);

        // Pasting the same research again has nothing left to fill.
        var again = Results(await client.GraphQLAsync(CreateResearched, new
        {
            companies = new[] { Researched(name, null, [ListingTests.Unique("Rust")]) },
        })).Single();
        Assert.False(again.GetProperty("filled").GetBoolean());
        Assert.Equal([skill], SkillNames(again.GetProperty("company")));
    }

    [Fact]
    public async Task CreateCompanies_MatchesByOrgNumberBeforeName()
    {
        var client = await factory.GetOwnerClientAsync();
        var (name, renamed) = (ListingTests.Unique("Acme"), ListingTests.Unique("Acme Norge"));
        var org = OrgNumber();
        var id = Results(await client.GraphQLAsync(CreateResearched, new { companies = new[] { Researched(name, org) } }))
            .Single().GetProperty("company").GetProperty("id").GetInt32();

        var data = await client.GraphQLAsync(CreateResearched, new
        {
            companies = new[] { Researched(renamed, org), Researched(ListingTests.Unique("Other"), org) },
        });
        var results = Results(data);

        Assert.All(results, r => Assert.False(r.GetProperty("created").GetBoolean()));
        Assert.All(results, r => Assert.Equal(id, r.GetProperty("company").GetProperty("id").GetInt32()));
        Assert.Equal(0, await CountAsync(client, renamed));
    }

    [Fact]
    public async Task CreateCompanies_ByNameWithOtherOrgNumber_KeepsTheExistingOne()
    {
        var client = await factory.GetOwnerClientAsync();
        var name = ListingTests.Unique("Acme");
        var org = OrgNumber();
        await client.GraphQLAsync(CreateResearched, new { companies = new[] { Researched(name, org) } });

        var result = Results(await client.GraphQLAsync(CreateResearched, new
        {
            companies = new[] { Researched(name, OrgNumber()) },
        })).Single();

        Assert.False(result.GetProperty("filled").GetBoolean());
        Assert.Equal(org, result.GetProperty("company").GetProperty("orgNumber").GetString());
    }

    [Fact]
    public async Task CreateCompanies_SharesSkillsWithListingsAndBatchRows()
    {
        var client = await factory.GetOwnerClientAsync();
        var skill = ListingTests.Unique("Kubernetes");
        var created = await client.GraphQLAsync(CreateResearched, new { companies = new[] { Researched(ListingTests.Unique("Seed"), null, [skill]) } });
        var skillId = Results(created).Single().GetProperty("company").GetProperty("skills")[0]
            .GetProperty("skill").GetProperty("id").GetInt32();
        var fresh = ListingTests.Unique("Terraform");

        var results = Results(await client.GraphQLAsync(CreateResearched, new
        {
            companies = new[]
            {
                Researched(ListingTests.Unique("A"), null, [skill.ToUpperInvariant(), fresh]),
                Researched(ListingTests.Unique("B"), null, [fresh.ToLowerInvariant()]),
            },
        }));

        int SkillId(JsonElement result, string name) => result.GetProperty("company").GetProperty("skills").EnumerateArray()
            .Single(s => string.Equals(s.GetProperty("skill").GetProperty("name").GetString(), name, StringComparison.OrdinalIgnoreCase))
            .GetProperty("skill").GetProperty("id").GetInt32();
        Assert.Equal(skillId, SkillId(results[0], skill));
        Assert.Equal(SkillId(results[0], fresh), SkillId(results[1], fresh));
    }

    [Theory]
    [InlineData("123456789", "https://acme.example", 2010)]
    [InlineData(null, "javascript:alert(1)", 2010)]
    [InlineData(null, "https://acme.example", 1700)]
    [InlineData(null, "https://acme.example", 3000)]
    public async Task CreateCompanies_WithInvalidResearch_SavesNothing(string? orgNumber, string websiteUrl, int foundedYear)
    {
        var client = await factory.GetOwnerClientAsync();
        var marker = Guid.NewGuid().ToString("N");

        var data = await client.GraphQLAsync(CreateResearched, new
        {
            companies = new[] { Researched($"{marker} 1"), Researched($"{marker} 2", orgNumber, null, websiteUrl, foundedYear) },
        });

        Assert.Equal("InvalidCompanyError",
            data.GetProperty("createCompanies").GetProperty("errors")[0].GetProperty("__typename").GetString());
        Assert.Equal(0, await CountAsync(client, marker));
    }

    [Fact]
    public async Task CreateCompanies_DoesNotMatchAnotherUsersOrgNumber()
    {
        var owner = await factory.GetOwnerClientAsync();
        var other = await factory.GetSecondOwnerClientAsync();
        var name = ListingTests.Unique("Acme");
        var org = OrgNumber();
        var ownerId = Results(await owner.GraphQLAsync(CreateResearched, new { companies = new[] { Researched(name, org) } }))
            .Single().GetProperty("company").GetProperty("id").GetInt32();

        var result = Results(await other.GraphQLAsync(CreateResearched, new { companies = new[] { Researched(name, org) } })).Single();

        Assert.True(result.GetProperty("created").GetBoolean());
        Assert.NotEqual(ownerId, result.GetProperty("company").GetProperty("id").GetInt32());
    }

    [Fact]
    public async Task UpdateCompanyDetails_ReplacesFieldsAndSkills()
    {
        var client = await factory.GetOwnerClientAsync();
        var id = await CompanyIdAsync(client, ListingTests.Unique("Acme"));
        var (kept, dropped, added) = (ListingTests.Unique("C#"), ListingTests.Unique("Java"), ListingTests.Unique("F#"));
        var org = OrgNumber();
        await client.GraphQLAsync(UpdateDetails, new
        {
            id, details = new { orgNumber = org, kind = "PRODUCT", description = "Old", skills = new[] { kept, dropped } },
        });

        var data = await client.GraphQLAsync(UpdateDetails, new
        {
            id, details = new { websiteUrl = "https://acme.example", foundedYear = 2001, skills = new[] { kept, added } },
        });
        var company = data.GetProperty("updateCompanyDetails").GetProperty("company");

        Assert.Equal(JsonValueKind.Null, company.GetProperty("orgNumber").ValueKind);
        Assert.Equal(JsonValueKind.Null, company.GetProperty("kind").ValueKind);
        Assert.Equal(JsonValueKind.Null, company.GetProperty("description").ValueKind);
        Assert.Equal("https://acme.example", company.GetProperty("websiteUrl").GetString());
        Assert.Equal(2001, company.GetProperty("foundedYear").GetInt32());
        Assert.Equal(new[] { added, kept }.Order(), SkillNames(company));
    }

    [Fact]
    public async Task UpdateCompanyDetails_WithTakenOrgNumberOrMissingCompany_ReturnsErrors()
    {
        var client = await factory.GetOwnerClientAsync();
        var org = OrgNumber();
        await client.GraphQLAsync(CreateResearched, new { companies = new[] { Researched(ListingTests.Unique("Taken"), org) } });
        var id = await CompanyIdAsync(client, ListingTests.Unique("Acme"));

        var taken = await client.GraphQLAsync(UpdateDetails, new { id, details = new { orgNumber = org, skills = Array.Empty<string>() } });
        var missing = await client.GraphQLAsync(UpdateDetails, new { id = int.MaxValue, details = new { skills = Array.Empty<string>() } });
        var otherUser = await (await factory.GetSecondOwnerClientAsync()).GraphQLAsync(UpdateDetails,
            new { id, details = new { skills = Array.Empty<string>() } });

        Assert.Equal("InvalidCompanyError",
            taken.GetProperty("updateCompanyDetails").GetProperty("errors")[0].GetProperty("__typename").GetString());
        Assert.Equal("CompanyNotFoundError",
            missing.GetProperty("updateCompanyDetails").GetProperty("errors")[0].GetProperty("__typename").GetString());
        Assert.Equal("CompanyNotFoundError",
            otherUser.GetProperty("updateCompanyDetails").GetProperty("errors")[0].GetProperty("__typename").GetString());
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

    private const string SetAvoided = """
        mutation ($id: Int!, $isAvoided: Boolean!) {
          setCompanyAvoided(input: { id: $id, isAvoided: $isAvoided }) {
            company { id avoidedAt favoritedAt notes }
            errors { __typename }
          }
        }
        """;

    private const string SetNotes = """
        mutation ($id: Int!, $notes: String) {
          setCompanyNotes(input: { id: $id, notes: $notes }) {
            company { id avoidedAt favoritedAt notes }
            errors { __typename }
          }
        }
        """;

    [Fact]
    public async Task SetCompanyAvoided_SetsClearsAndKeepsFirstTime()
    {
        var client = await factory.GetOwnerClientAsync();
        var id = await CompanyIdAsync(client, ListingTests.Unique("Acme"));

        var first = await AvoidAsync(client, id, true);
        var again = await AvoidAsync(client, id, true);
        var removed = await AvoidAsync(client, id, false);

        Assert.NotEqual(JsonValueKind.Null, first.GetProperty("avoidedAt").ValueKind);
        Assert.Equal(first.GetProperty("avoidedAt").GetString(), again.GetProperty("avoidedAt").GetString());
        Assert.Equal(JsonValueKind.Null, removed.GetProperty("avoidedAt").ValueKind);
    }

    [Fact]
    public async Task SetCompanyAvoidedAndNotes_OnUnknownOrOtherUsersCompany_ReturnCompanyNotFoundError()
    {
        var owner = await factory.GetOwnerClientAsync();
        var guest = await factory.GetGuestClientAsync();
        var id = await CompanyIdAsync(owner, ListingTests.Unique("Acme"));

        foreach (var (mutation, data) in new[]
                 {
                     ("setCompanyAvoided", await guest.GraphQLAsync(SetAvoided, new { id, isAvoided = true })),
                     ("setCompanyAvoided", await owner.GraphQLAsync(SetAvoided, new { id = int.MaxValue, isAvoided = true })),
                     ("setCompanyNotes", await guest.GraphQLAsync(SetNotes, new { id, notes = "x" })),
                     ("setCompanyNotes", await owner.GraphQLAsync(SetNotes, new { id = int.MaxValue, notes = "x" })),
                 })
            Assert.Equal("CompanyNotFoundError",
                data.GetProperty(mutation).GetProperty("errors")[0].GetProperty("__typename").GetString());
    }

    [Fact]
    public async Task Companies_FilterByAvoided_ExcludesOthers()
    {
        var client = await factory.GetOwnerClientAsync();
        var avoided = await CompanyIdAsync(client, ListingTests.Unique("Acme"));
        var other = await CompanyIdAsync(client, ListingTests.Unique("Bravo"));
        await AvoidAsync(client, avoided, true);

        var data = await client.GraphQLAsync("{ companies(where: { avoidedAt: { neq: null } }) { nodes { id } } }");
        var ids = data.GetProperty("companies").GetProperty("nodes").EnumerateArray()
            .Select(n => n.GetProperty("id").GetInt32()).ToList();

        Assert.Contains(avoided, ids);
        Assert.DoesNotContain(other, ids);
    }

    [Fact]
    public async Task AvoidedFavoriteAndNotes_DontChangeEachOther()
    {
        var client = await factory.GetOwnerClientAsync();
        var id = await CompanyIdAsync(client, ListingTests.Unique("Acme"));
        var favorited = (await FavoriteAsync(client, id, true)).GetProperty("favoritedAt").GetString();

        var avoided = await AvoidAsync(client, id, true);
        Assert.Equal(favorited, avoided.GetProperty("favoritedAt").GetString());

        var noted = await NotesAsync(client, id, "Good mentors");
        Assert.Equal(favorited, noted.GetProperty("favoritedAt").GetString());
        Assert.Equal(avoided.GetProperty("avoidedAt").GetString(), noted.GetProperty("avoidedAt").GetString());

        var unfavorited = await FavoriteAsync(client, id, false);
        var after = await AvoidAsync(client, id, true);
        Assert.Equal(JsonValueKind.Null, unfavorited.GetProperty("favoritedAt").ValueKind);
        Assert.Equal(avoided.GetProperty("avoidedAt").GetString(), after.GetProperty("avoidedAt").GetString());
        Assert.Equal("Good mentors", after.GetProperty("notes").GetString());
    }

    [Fact]
    public async Task AvoidedAndNotes_SurviveUpdateCompanyAndCreateCompanies()
    {
        var client = await factory.GetOwnerClientAsync();
        var name = ListingTests.Unique("Acme");
        var id = await CompanyIdAsync(client, name);
        var avoidedAt = (await AvoidAsync(client, id, true)).GetProperty("avoidedAt").GetString();
        await NotesAsync(client, id, "Three reorgs in a year");

        await client.GraphQLAsync(UpdateCompany, new
        {
            id, name = name + " AS", industryName = (string?)null, locations = Array.Empty<object>(),
        });
        await client.GraphQLAsync("""
            mutation ($name: String!) {
              createCompanies(input: { companies: [{ name: $name, locations: [] }] }) { errors { __typename } }
            }
            """, new { name = name + " AS" });

        var after = (await client.GraphQLAsync("""
            query ($id: Int!) { companies(where: { id: { eq: $id } }) { nodes { avoidedAt notes } } }
            """, new { id })).GetProperty("companies").GetProperty("nodes")[0];
        Assert.Equal(avoidedAt, after.GetProperty("avoidedAt").GetString());
        Assert.Equal("Three reorgs in a year", after.GetProperty("notes").GetString());
    }

    [Fact]
    public async Task ListingsOfAvoidedCompany_StayVisibleWithAvoidedAt()
    {
        var client = await factory.GetOwnerClientAsync();
        var name = ListingTests.Unique("Acme");
        var id = await CompanyIdAsync(client, name);
        await AvoidAsync(client, id, true);

        var second = await ListingTests.CreateAsync(client, ListingTests.NewListing(name));
        var data = await client.GraphQLAsync("""
            query ($id: Int!) { listings(where: { company: { id: { eq: $id } } }) { items { company { avoidedAt } } } }
            """, new { id });
        var nodes = data.GetProperty("listings").GetProperty("items").EnumerateArray().ToList();

        Assert.Equal(id, second.GetProperty("company").GetProperty("id").GetInt32());
        Assert.Equal(2, nodes.Count);
        Assert.All(nodes, n =>
            Assert.NotEqual(JsonValueKind.Null, n.GetProperty("company").GetProperty("avoidedAt").ValueKind));
    }

    [Fact]
    public async Task SetCompanyNotes_TrimsReplacesAndClearsBlank()
    {
        var client = await factory.GetOwnerClientAsync();
        var id = await CompanyIdAsync(client, ListingTests.Unique("Acme"));

        Assert.Equal("Avoid: three reorgs in a year",
            (await NotesAsync(client, id, "  Avoid: three reorgs in a year  ")).GetProperty("notes").GetString());
        Assert.Equal("Changed my mind", (await NotesAsync(client, id, "Changed my mind")).GetProperty("notes").GetString());
        Assert.Equal(JsonValueKind.Null, (await NotesAsync(client, id, "   ")).GetProperty("notes").ValueKind);
        await NotesAsync(client, id, "Back");
        Assert.Equal(JsonValueKind.Null, (await NotesAsync(client, id, null)).GetProperty("notes").ValueKind);
    }

    [Fact]
    public async Task SetCompanyNotes_TooLong_ReturnsInvalidCompanyErrorAndKeepsNotes()
    {
        var client = await factory.GetOwnerClientAsync();
        var id = await CompanyIdAsync(client, ListingTests.Unique("Acme"));
        await NotesAsync(client, id, "Keep me");

        var data = await client.GraphQLAsync(SetNotes, new { id, notes = new string('x', 10_001) });
        var after = await client.GraphQLAsync("""
            query ($id: Int!) { companies(where: { id: { eq: $id } }) { nodes { notes } } }
            """, new { id });

        Assert.Equal("InvalidCompanyError",
            data.GetProperty("setCompanyNotes").GetProperty("errors")[0].GetProperty("__typename").GetString());
        Assert.Equal("Keep me", after.GetProperty("companies").GetProperty("nodes")[0].GetProperty("notes").GetString());
    }

    private static async Task<JsonElement> AvoidAsync(HttpClient client, int id, bool isAvoided) =>
        (await client.GraphQLAsync(SetAvoided, new { id, isAvoided }))
        .GetProperty("setCompanyAvoided").GetProperty("company");

    private static async Task<JsonElement> NotesAsync(HttpClient client, int id, string? notes) =>
        (await client.GraphQLAsync(SetNotes, new { id, notes }))
        .GetProperty("setCompanyNotes").GetProperty("company");

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
