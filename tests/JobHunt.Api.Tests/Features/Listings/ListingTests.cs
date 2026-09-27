using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace JobHunt.Api.Tests.Features.Listings;

// Tests share one DB and one owner, so each test uses its own company and skill names.
public class ListingTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string ListingFields = """
        id title seniority employmentType schedule workMode description url
        postedOn deadlineType deadlineOn postingLanguage createdAt updatedAt
        company { id name location { city countryCode } }
        jobRole { id name }
        skills { level minYears isRequired skill { id name } }
        languages { language level isRequired }
        locations { location { id city countryCode } }
        """;

    private const string CreateListing = $$"""
        mutation ($listing: ListingInput!) {
          createListing(input: { listing: $listing }) {
            listing { {{ListingFields}} }
            errors { __typename ... on Error { message } }
          }
        }
        """;

    private const string UpdateListing = $$"""
        mutation ($id: Int!, $listing: ListingInput!) {
          updateListing(input: { id: $id, listing: $listing }) {
            listing { {{ListingFields}} }
            errors { __typename ... on Error { message } }
          }
        }
        """;

    private const string DeleteListing = """
        mutation ($id: Int!) {
          deleteListing(input: { id: $id }) { id errors { __typename } }
        }
        """;

    private const string ListingById = """
        query ($id: Int!) { listingById(id: $id) { id title } }
        """;

    [Fact]
    public async Task CreateListing_SavesAllFieldsAndAppliesDefaults()
    {
        var client = await factory.GetOwnerClientAsync();
        var company = Unique("Nordlys Tech AS");

        var data = await client.GraphQLAsync(CreateListing, new
        {
            listing = new
            {
                title = "Backend-utvikler (.NET)",
                companyName = company,
                jobRoleName = "Backend Developer",
                seniority = "SENIOR",
                employmentType = "PERMANENT",
                schedule = "FULL_TIME",
                workMode = "HYBRID",
                description = "Du blir del av et team som…",
                url = "https://www.finn.no/job/123",
                postedOn = "2026-09-20",
                deadlineType = "DATE",
                deadlineOn = "2026-10-15",
                postingLanguage = "NORWEGIAN",
                skills = new object[]
                {
                    new { name = "C#", level = 4, minYears = 5 },
                    new { name = "ASP.NET Core" },
                    new { name = "Docker", level = 2, isRequired = false },
                },
                languages = new object[]
                {
                    new { language = "NORWEGIAN", level = "B2" },
                    new { language = "ENGLISH", level = "C1" },
                },
                locations = new object[] { new { city = "Oslo", countryCode = "NO" } },
            },
        });

        var payload = data.GetProperty("createListing");
        Assert.Equal(JsonValueKind.Null, payload.GetProperty("errors").ValueKind);
        var listing = payload.GetProperty("listing");
        Assert.Equal("Backend-utvikler (.NET)", listing.GetProperty("title").GetString());
        Assert.Equal(company, listing.GetProperty("company").GetProperty("name").GetString());
        Assert.Equal("Backend Developer", listing.GetProperty("jobRole").GetProperty("name").GetString());
        Assert.Equal("SENIOR", listing.GetProperty("seniority").GetString());
        Assert.Equal("FULL_TIME", listing.GetProperty("schedule").GetString());
        Assert.Equal("2026-10-15", listing.GetProperty("deadlineOn").GetString());
        Assert.Equal("NORWEGIAN", listing.GetProperty("postingLanguage").GetString());

        var skills = listing.GetProperty("skills").EnumerateArray()
            .ToDictionary(s => s.GetProperty("skill").GetProperty("name").GetString()!);
        Assert.Equal(4, skills["C#"].GetProperty("level").GetInt32());
        Assert.Equal(5, skills["C#"].GetProperty("minYears").GetInt32());
        Assert.Equal(JsonValueKind.Null, skills["ASP.NET Core"].GetProperty("minYears").ValueKind);
        Assert.Equal(3, skills["ASP.NET Core"].GetProperty("level").GetInt32());
        Assert.True(skills["ASP.NET Core"].GetProperty("isRequired").GetBoolean());
        Assert.False(skills["Docker"].GetProperty("isRequired").GetBoolean());

        var languages = listing.GetProperty("languages").EnumerateArray()
            .ToDictionary(l => l.GetProperty("language").GetString()!);
        Assert.Equal("B2", languages["NORWEGIAN"].GetProperty("level").GetString());
        Assert.True(languages["ENGLISH"].GetProperty("isRequired").GetBoolean());

        var location = Assert.Single(listing.GetProperty("locations").EnumerateArray()).GetProperty("location");
        Assert.Equal("Oslo", location.GetProperty("city").GetString());
        Assert.Equal("NO", location.GetProperty("countryCode").GetString());
    }

    [Fact]
    public async Task CreateListing_ReusesCompanyRoleAndSkillsCaseInsensitively()
    {
        var client = await factory.GetOwnerClientAsync();
        var company = Unique("Fjordkraft Data");
        var skill = Unique("Kotlin");

        var first = await CreateAsync(client, NewListing(company, jobRoleName: "Backend Developer",
            skills: [new { name = skill }]));
        var second = await CreateAsync(client, NewListing(company.ToLowerInvariant(), jobRoleName: "backend developer",
            skills: [new { name = skill.ToUpperInvariant() }]));

        Assert.Equal(Id(first, "company"), Id(second, "company"));
        Assert.Equal(Id(first, "jobRole"), Id(second, "jobRole"));
        Assert.Equal(SkillIds(first), SkillIds(second));

        var data = await client.GraphQLAsync("""
            query ($company: String!, $skill: String!) {
              companies(search: $company) { nodes { name listingCount } }
              skills(search: $skill) { nodes { name listingCount } }
            }
            """, new { company = company.ToLowerInvariant(), skill = skill.ToLowerInvariant() });

        var companyNode = Assert.Single(data.GetProperty("companies").GetProperty("nodes").EnumerateArray());
        Assert.Equal(company, companyNode.GetProperty("name").GetString());
        Assert.Equal(2, companyNode.GetProperty("listingCount").GetInt32());
        var skillNode = Assert.Single(data.GetProperty("skills").GetProperty("nodes").EnumerateArray());
        Assert.Equal(skill, skillNode.GetProperty("name").GetString());
        Assert.Equal(2, skillNode.GetProperty("listingCount").GetInt32());
    }

    [Fact]
    public async Task CreateListing_WithSkillLevelOutOfRange_ReturnsInvalidListingError()
    {
        var client = await factory.GetOwnerClientAsync();

        var data = await client.GraphQLAsync(CreateListing,
            new { listing = NewListing(Unique("Acme"), skills: [new { name = "C#", level = 7 }]) });

        var error = Assert.Single(data.GetProperty("createListing").GetProperty("errors").EnumerateArray());
        Assert.Equal("InvalidListingError", error.GetProperty("__typename").GetString());
        Assert.Contains("1–5", error.GetProperty("message").GetString());
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("not a url")]
    [InlineData("  ")]
    public async Task CreateListing_WithNonHttpUrl_ReturnsInvalidListingError(string url)
    {
        var client = await factory.GetOwnerClientAsync();

        var data = await client.GraphQLAsync(CreateListing,
            new { listing = NewListing(Unique("Acme"), url: url) });

        var error = Assert.Single(data.GetProperty("createListing").GetProperty("errors").EnumerateArray());
        Assert.Equal("InvalidListingError", error.GetProperty("__typename").GetString());
    }

    [Fact]
    public async Task CreateListing_WithFuturePostedDate_ReturnsInvalidListingError()
    {
        var client = await factory.GetOwnerClientAsync();

        var data = await client.GraphQLAsync(CreateListing,
            new { listing = NewListing(Unique("Acme"), postedOn: "2999-01-01") });

        var error = Assert.Single(data.GetProperty("createListing").GetProperty("errors").EnumerateArray());
        Assert.Equal("InvalidListingError", error.GetProperty("__typename").GetString());
    }

    [Fact]
    public async Task CreateListing_WithDateDeadlineButNoDate_ReturnsInvalidListingError()
    {
        var client = await factory.GetOwnerClientAsync();

        var data = await client.GraphQLAsync(CreateListing,
            new { listing = NewListing(Unique("Acme"), deadlineType: "DATE") });

        var error = Assert.Single(data.GetProperty("createListing").GetProperty("errors").EnumerateArray());
        Assert.Equal("InvalidListingError", error.GetProperty("__typename").GetString());
    }

    [Fact]
    public async Task UpdateListing_ReplacesSkillsAndLanguages()
    {
        var client = await factory.GetOwnerClientAsync();
        var company = Unique("Acme");
        var created = await CreateAsync(client, NewListing(company,
            skills: [new { name = "C#", level = 4 }, new { name = "Docker" }],
            languages: [new { language = "NORWEGIAN", level = "B2" }]));

        var data = await client.GraphQLAsync(UpdateListing, new
        {
            id = created.GetProperty("id").GetInt32(),
            listing = NewListing(company, title: "Senior Backend Developer", deadlineType: "ASAP",
                skills: [new { name = "C#", level = 2 }, new { name = "SQL" }],
                languages: [new { language = "ENGLISH", level = "C1" }]),
        });

        var listing = data.GetProperty("updateListing").GetProperty("listing");
        Assert.Equal("Senior Backend Developer", listing.GetProperty("title").GetString());
        Assert.Equal("ASAP", listing.GetProperty("deadlineType").GetString());
        var skills = listing.GetProperty("skills").EnumerateArray()
            .ToDictionary(s => s.GetProperty("skill").GetProperty("name").GetString()!, s => s.GetProperty("level").GetInt32());
        Assert.Equal(new Dictionary<string, int> { ["C#"] = 2, ["SQL"] = 3 }, skills);
        var language = Assert.Single(listing.GetProperty("languages").EnumerateArray());
        Assert.Equal("ENGLISH", language.GetProperty("language").GetString());
    }

    [Fact]
    public async Task DeleteListing_RemovesListing()
    {
        var client = await factory.GetOwnerClientAsync();
        var id = (await CreateAsync(client, NewListing(Unique("Acme")))).GetProperty("id").GetInt32();

        var deleted = await client.GraphQLAsync(DeleteListing, new { id });
        Assert.Equal(id, deleted.GetProperty("deleteListing").GetProperty("id").GetInt32());

        var byId = await client.GraphQLAsync(ListingById, new { id });
        Assert.Equal(JsonValueKind.Null, byId.GetProperty("listingById").ValueKind);

        var again = await client.GraphQLAsync(DeleteListing, new { id });
        Assert.Equal("ListingNotFoundError",
            again.GetProperty("deleteListing").GetProperty("errors")[0].GetProperty("__typename").GetString());
    }

    [Fact]
    public async Task Listings_AreInvisibleToOtherUsers()
    {
        var owner = await factory.GetOwnerClientAsync();
        var guest = await factory.GetGuestClientAsync();
        var company = Unique("Secret AS");
        var id = (await CreateAsync(owner, NewListing(company))).GetProperty("id").GetInt32();

        var data = await guest.GraphQLAsync("""
            query ($id: Int!, $company: String!) {
              listingById(id: $id) { id }
              listings { items { id } }
              companies(search: $company) { nodes { id } }
            }
            """, new { id, company });

        Assert.Equal(JsonValueKind.Null, data.GetProperty("listingById").ValueKind);
        Assert.DoesNotContain(data.GetProperty("listings").GetProperty("items").EnumerateArray(),
            n => n.GetProperty("id").GetInt32() == id);
        Assert.Empty(data.GetProperty("companies").GetProperty("nodes").EnumerateArray());

        var update = await guest.GraphQLAsync(UpdateListing, new { id, listing = NewListing(company) });
        Assert.Equal("ListingNotFoundError",
            update.GetProperty("updateListing").GetProperty("errors")[0].GetProperty("__typename").GetString());
        var delete = await guest.GraphQLAsync(DeleteListing, new { id });
        Assert.Equal("ListingNotFoundError",
            delete.GetProperty("deleteListing").GetProperty("errors")[0].GetProperty("__typename").GetString());
    }

    [Fact]
    public async Task UpdateCompany_SetsLocation()
    {
        var client = await factory.GetOwnerClientAsync();
        var created = await CreateAsync(client, NewListing(Unique("Acme")));

        var data = await client.GraphQLAsync("""
            mutation ($id: Int!) {
              updateCompany(input: { id: $id, name: "Acme Renamed", location: { city: "Bergen", countryCode: "NO" } }) {
                company { name location { city countryCode } }
              }
            }
            """, new { id = Id(created, "company") });

        var company = data.GetProperty("updateCompany").GetProperty("company");
        Assert.Equal("Acme Renamed", company.GetProperty("name").GetString());
        Assert.Equal("Bergen", company.GetProperty("location").GetProperty("city").GetString());
    }

    [Fact]
    public async Task GraphQL_WhenAnonymous_Returns401()
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/graphql", new { query = "{ listings { items { id } } }" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    internal static string Unique(string name) => $"{name} {Guid.NewGuid():N}";

    internal static object NewListing(
        string companyName,
        string title = "Backend Developer",
        string jobRoleName = "Backend Developer",
        string deadlineType = "UNSPECIFIED",
        string url = "https://www.finn.no/job/123",
        string postedOn = "2026-09-20",
        string? deadlineOn = null,
        object[]? skills = null,
        object[]? languages = null,
        object[]? locations = null) =>
        new
        {
            title, companyName, jobRoleName, deadlineType, deadlineOn, url, postedOn,
            skills = skills ?? [], languages = languages ?? [], locations = locations ?? [],
        };

    internal static async Task<JsonElement> CreateAsync(HttpClient client, object listing)
    {
        var data = await client.GraphQLAsync(CreateListing, new { listing });
        return data.GetProperty("createListing").GetProperty("listing");
    }

    private static int Id(JsonElement listing, string relation) =>
        listing.GetProperty(relation).GetProperty("id").GetInt32();

    private static int[] SkillIds(JsonElement listing) =>
        listing.GetProperty("skills").EnumerateArray()
            .Select(s => s.GetProperty("skill").GetProperty("id").GetInt32()).Order().ToArray();
}
