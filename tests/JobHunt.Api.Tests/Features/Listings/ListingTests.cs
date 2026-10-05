using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace JobHunt.Api.Tests.Features.Listings;

// Tests share one DB and one owner, so each test uses its own company and skill names.
public class ListingTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string ListingFields = """
        id title seniority employmentType schedule workMode description url
        postedOn deadlineType deadlineOn postingLanguage createdAt updatedAt analyzedAt
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

    private const string CreateListingWithDuplicates = """
        mutation ($listing: ListingInput!) {
          createListing(input: { listing: $listing }) {
            listing { id }
            previousAttempts { id }
            possibleDuplicates { id }
            errors { __typename ... on DuplicateListingError { existingListingId } }
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
                url = UniqueUrl(),
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
    public async Task CreateListing_StoresUrlWithoutTrackingParamsFragmentOrTrailingSlash()
    {
        var client = await factory.GetOwnerClientAsync();
        var id = Guid.NewGuid().ToString("N");

        var listing = await CreateAsync(client, NewListing(Unique("Acme"),
            url: $"https://WWW.Finn.no/job/ad/?finnkode={id}&utm_source=mail&fbclid=x#apply"));

        Assert.Equal($"https://www.finn.no/job/ad?finnkode={id}", listing.GetProperty("url").GetString());
    }

    [Fact]
    public async Task CreateListing_WithSameUrlAsArchivedListing_ReturnsDuplicateListingError()
    {
        var client = await factory.GetOwnerClientAsync();
        var url = UniqueUrl();
        var existing = await CreateAsync(client, NewListing(Unique("Acme"), url: url));
        var existingId = existing.GetProperty("id").GetInt32();
        await client.GraphQLAsync(
            "mutation ($id: Int!) { setListingArchived(input: { id: $id, isArchived: true }) { listing { id } } }",
            new { id = existingId });

        var data = await client.GraphQLAsync(CreateListingWithDuplicates,
            new { listing = NewListing(Unique("Other"), url: url + "/?utm_campaign=x") });

        var payload = data.GetProperty("createListing");
        Assert.Equal(JsonValueKind.Null, payload.GetProperty("listing").ValueKind);
        var error = Assert.Single(payload.GetProperty("errors").EnumerateArray());
        Assert.Equal("DuplicateListingError", error.GetProperty("__typename").GetString());
        Assert.Equal(existingId, error.GetProperty("existingListingId").GetInt32());
    }

    // Pins ApplicationStatuses.Final (the C# check) for the open statuses.
    [Theory]
    [InlineData("SAVED")]
    [InlineData("APPLIED")]
    [InlineData("INTERVIEWING")]
    [InlineData("OFFER")]
    public async Task CreateListing_WithSameUrlAsOpenListing_ReturnsDuplicateListingError(string status)
    {
        var client = await factory.GetOwnerClientAsync();
        var url = UniqueUrl();
        var existingId = await ListingWithStatusAsync(client, url, status);

        var data = await client.GraphQLAsync(CreateListingWithDuplicates, new { listing = NewListing(Unique("Other"), url: url) });

        var error = Assert.Single(data.GetProperty("createListing").GetProperty("errors").EnumerateArray());
        Assert.Equal("DuplicateListingError", error.GetProperty("__typename").GetString());
        Assert.Equal(existingId, error.GetProperty("existingListingId").GetInt32());
    }

    // Pins the index filter for the final statuses: the database must not block the new attempt.
    [Theory]
    [InlineData("REJECTED")]
    [InlineData("WITHDRAWN")]
    [InlineData("ACCEPTED")]
    public async Task CreateListing_WithSameUrlAsClosedListing_CreatesNewAttempt(string status)
    {
        var client = await factory.GetOwnerClientAsync();
        var url = UniqueUrl();
        var closedId = await ListingWithStatusAsync(client, url, status);

        var created = await CreateAsync(client, NewListing(Unique("Acme"), url: url));

        Assert.NotEqual(closedId, created.GetProperty("id").GetInt32());
        var data = await client.GraphQLAsync("""
            query ($closed: Int!, $created: Int!) { closed: listingById(id: $closed) { status } created: listingById(id: $created) { status } }
            """, new { closed = closedId, created = created.GetProperty("id").GetInt32() });
        Assert.Equal(status, data.GetProperty("closed").GetProperty("status").GetString());
        Assert.Equal("SAVED", data.GetProperty("created").GetProperty("status").GetString());
    }

    [Fact]
    public async Task UpdateListing_ToAnotherListingsUrl_ReturnsDuplicateListingError()
    {
        var client = await factory.GetOwnerClientAsync();
        var company = Unique("Acme");
        var first = await CreateAsync(client, NewListing(company));
        var second = await CreateAsync(client, NewListing(company));
        var url = first.GetProperty("url").GetString()!;

        // Re-saving a listing with its own URL is not a duplicate.
        await UpdateAsync(client, first.GetProperty("id").GetInt32(), NewListing(company, url: url));
        var data = await client.GraphQLAsync(UpdateListing,
            new { id = second.GetProperty("id").GetInt32(), listing = NewListing(company, url: url) });

        var error = Assert.Single(data.GetProperty("updateListing").GetProperty("errors").EnumerateArray());
        Assert.Equal("DuplicateListingError", error.GetProperty("__typename").GetString());
    }

    [Fact]
    public async Task CreateListing_AtClosedListingsUrl_ReturnsItAsPreviousAttemptOnly()
    {
        var client = await factory.GetOwnerClientAsync();
        var company = Unique("Acme");
        var url = UniqueUrl();
        var older = await ListingWithStatusAsync(client, url, "WITHDRAWN", company);
        var newer = await ListingWithStatusAsync(client, url, "REJECTED", company);

        // Same company and role too, so it would also match as a possible duplicate.
        var payload = (await client.GraphQLAsync(CreateListingWithDuplicates, new { listing = NewListing(company, url: url) }))
            .GetProperty("createListing");

        Assert.Equal([newer, older], IdsOf(payload, "previousAttempts"));
        Assert.Empty(IdsOf(payload, "possibleDuplicates"));
    }

    [Fact]
    public async Task CreateListing_FirstAttempt_ReturnsNoPreviousAttempts()
    {
        var client = await factory.GetOwnerClientAsync();

        var payload = (await client.GraphQLAsync(CreateListingWithDuplicates, new { listing = NewListing(Unique("Acme")) }))
            .GetProperty("createListing");

        Assert.Empty(IdsOf(payload, "previousAttempts"));
    }

    [Fact]
    public async Task UpdateListing_ClosedListingToOpenListingsUrl_Succeeds()
    {
        var client = await factory.GetOwnerClientAsync();
        var company = Unique("Acme");
        var url = UniqueUrl();
        await ListingWithStatusAsync(client, url, "SAVED", company);
        var closedId = await ListingWithStatusAsync(client, UniqueUrl(), "REJECTED", company);

        var data = await client.GraphQLAsync(UpdateListing, new { id = closedId, listing = NewListing(company, url: url) });

        var payload = data.GetProperty("updateListing");
        Assert.Equal(JsonValueKind.Null, payload.GetProperty("errors").ValueKind);
        Assert.Equal(url, payload.GetProperty("listing").GetProperty("url").GetString());
    }

    [Fact]
    public async Task CreateListing_ReturnsSameCompanyListingsWithSameRoleOrTitleAsPossibleDuplicates()
    {
        var client = await factory.GetOwnerClientAsync();
        var company = Unique("Acme");
        var sameRole = await CreateAsync(client, NewListing(company, title: "Utvikler", jobRoleName: "Frontend Developer"));
        var sameTitle = await CreateAsync(client, NewListing(company, title: "frontendutvikler", jobRoleName: "Designer"));
        await CreateAsync(client, NewListing(company, title: "Lagermedarbeider", jobRoleName: "Warehouse Worker"));
        await CreateAsync(client, NewListing(Unique("Other"), title: "Frontendutvikler", jobRoleName: "Frontend Developer"));

        var data = await client.GraphQLAsync(CreateListingWithDuplicates,
            new { listing = NewListing(company, title: "Frontendutvikler ", jobRoleName: "Frontend Developer") });

        var ids = data.GetProperty("createListing").GetProperty("possibleDuplicates").EnumerateArray()
            .Select(l => l.GetProperty("id").GetInt32()).Order();
        Assert.Equal([sameRole.GetProperty("id").GetInt32(), sameTitle.GetProperty("id").GetInt32()], ids);
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
    public async Task UpdateListing_SetsAnalyzedAtOnceAndClearsIt()
    {
        var client = await factory.GetOwnerClientAsync();
        var company = Unique("Acme");
        var created = await CreateAsync(client, NewListing(company));
        var id = created.GetProperty("id").GetInt32();
        Assert.Equal(JsonValueKind.Null, created.GetProperty("analyzedAt").ValueKind);

        var analyzed = await UpdateAsync(client, id, NewListing(company, isAnalyzed: true));
        var analyzedAt = analyzed.GetProperty("analyzedAt").GetDateTime();

        var again = await UpdateAsync(client, id, NewListing(company, title: "Senior Backend Developer", isAnalyzed: true));
        Assert.Equal(analyzedAt, again.GetProperty("analyzedAt").GetDateTime());

        var cleared = await UpdateAsync(client, id, NewListing(company));
        Assert.Equal(JsonValueKind.Null, cleared.GetProperty("analyzedAt").ValueKind);
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
    public async Task ArchivedListings_AreHiddenUnlessAskedFor()
    {
        var client = await factory.GetOwnerClientAsync();
        var id = (await CreateAsync(client, NewListing(Unique("Acme")))).GetProperty("id").GetInt32();
        const string SetArchived = """
            mutation ($id: Int!, $isArchived: Boolean!) {
              setListingArchived(input: { id: $id, isArchived: $isArchived }) { listing { archivedAt } }
            }
            """;
        const string Lists = """
            query { active: listings(take: 100) { items { id } } archived: listings(archived: true, take: 100) { items { id } } }
            """;

        var archived = await client.GraphQLAsync(SetArchived, new { id, isArchived = true });
        Assert.NotEqual(JsonValueKind.Null,
            archived.GetProperty("setListingArchived").GetProperty("listing").GetProperty("archivedAt").ValueKind);
        var lists = await client.GraphQLAsync(Lists);
        Assert.DoesNotContain(Ids(lists, "active"), i => i == id);
        Assert.Contains(Ids(lists, "archived"), i => i == id);

        await client.GraphQLAsync(SetArchived, new { id, isArchived = false });
        lists = await client.GraphQLAsync(Lists);
        Assert.Contains(Ids(lists, "active"), i => i == id);
        Assert.DoesNotContain(Ids(lists, "archived"), i => i == id);

        static IEnumerable<int> Ids(JsonElement data, string list) =>
            data.GetProperty(list).GetProperty("items").EnumerateArray().Select(n => n.GetProperty("id").GetInt32());
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

    // URLs are unique per user and tests share one DB.
    internal static string UniqueUrl() => $"https://www.finn.no/job/{Guid.NewGuid():N}";

    internal static object NewListing(
        string companyName,
        string title = "Backend Developer",
        string jobRoleName = "Backend Developer",
        string deadlineType = "UNSPECIFIED",
        string? url = null,
        string postedOn = "2026-09-20",
        string? deadlineOn = null,
        object[]? skills = null,
        object[]? languages = null,
        object[]? locations = null,
        bool isAnalyzed = false) =>
        new
        {
            title, companyName, jobRoleName, deadlineType, deadlineOn, url = url ?? UniqueUrl(), postedOn, isAnalyzed,
            skills = skills ?? [], languages = languages ?? [], locations = locations ?? [],
        };

    internal static async Task<JsonElement> CreateAsync(HttpClient client, object listing)
    {
        var data = await client.GraphQLAsync(CreateListing, new { listing });
        return data.GetProperty("createListing").GetProperty("listing");
    }

    // Creates a listing with the URL and walks it to the status through the real mutations.
    internal static async Task<int> ListingWithStatusAsync(HttpClient client, string url, string status, string? company = null)
    {
        var listingId = (await CreateAsync(client, NewListing(company ?? Unique("Acme"), url: url))).GetProperty("id").GetInt32();
        if (status == "SAVED") return listingId;

        var cvDocumentId = await Documents.DocumentTests.UploadIdAsync(client, listingId);
        await client.GraphQLAsync("""
            mutation ($listingId: Int!, $cvDocumentId: Int!) {
              applyToListing(input: { listingId: $listingId, cvDocumentId: $cvDocumentId }) { listing { id } }
            }
            """, new { listingId, cvDocumentId });
        string[] outcomes = status switch
        {
            "APPLIED" or "INTERVIEWING" => [],
            "OFFER" => ["OFFER"],
            "ACCEPTED" => ["OFFER", "ACCEPTED"],
            _ => [status],
        };
        if (status == "INTERVIEWING")
            await client.GraphQLAsync("""
                mutation ($listingId: Int!) { addInterview(input: { listingId: $listingId, scheduledAt: "2026-10-01T10:00:00Z" }) { interview { id } } }
                """, new { listingId });
        foreach (var outcome in outcomes)
            await client.GraphQLAsync("""
                mutation ($listingId: Int!, $outcome: ApplicationOutcome!) {
                  recordOutcome(input: { listingId: $listingId, outcome: $outcome }) { listing { id } }
                }
                """, new { listingId, outcome });

        var data = await client.GraphQLAsync("query ($id: Int!) { listingById(id: $id) { status } }", new { id = listingId });
        Assert.Equal(status, data.GetProperty("listingById").GetProperty("status").GetString());
        return listingId;
    }

    private static async Task<JsonElement> UpdateAsync(HttpClient client, int id, object listing)
    {
        var data = await client.GraphQLAsync(UpdateListing, new { id, listing });
        return data.GetProperty("updateListing").GetProperty("listing");
    }

    private static int[] IdsOf(JsonElement payload, string list) =>
        payload.GetProperty(list).EnumerateArray().Select(l => l.GetProperty("id").GetInt32()).ToArray();

    private static int Id(JsonElement listing, string relation) =>
        listing.GetProperty(relation).GetProperty("id").GetInt32();

    private static int[] SkillIds(JsonElement listing) =>
        listing.GetProperty("skills").EnumerateArray()
            .Select(s => s.GetProperty("skill").GetProperty("id").GetInt32()).Order().ToArray();
}
