namespace JobHunt.Api.Tests.Features.Listings;

public class ListingSearchTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Search_IgnoresCaseIncludingNorwegianLetters()
    {
        var client = await factory.GetOwnerClientAsync();
        var company = ListingTests.Unique("Acme");
        await ListingTests.CreateAsync(client, ListingTests.NewListing(company, title: "Utvikler på Økonomi"));
        await ListingTests.CreateAsync(client, ListingTests.NewListing(company, title: "Lagermedarbeider"));

        Assert.Equal(["Utvikler på Økonomi"], await SearchAsync(client, "ØKONOMI", company));
        Assert.Equal(["Utvikler på Økonomi"], await SearchAsync(client, "økonomi", company));
    }

    [Fact]
    public async Task Search_MatchesCompanyAndJobRoleName()
    {
        var client = await factory.GetOwnerClientAsync();
        var company = ListingTests.Unique("Fjellbåt");
        var jobRole = ListingTests.Unique("Søknadsbehandler");
        await ListingTests.CreateAsync(client, ListingTests.NewListing(company, title: "by role", jobRoleName: jobRole));

        Assert.Equal(["by role"], await SearchAsync(client, company.ToLowerInvariant()[2..], company));
        Assert.Equal(["by role"], await SearchAsync(client, jobRole.ToUpperInvariant()[3..], company));
    }

    [Fact]
    public async Task Search_SkipsDescription()
    {
        var client = await factory.GetOwnerClientAsync();
        var company = ListingTests.Unique("Acme");
        var word = ListingTests.Unique("Kubernetes");
        await ListingTests.CreateAsync(client, new
        {
            title = "Backend Developer", companyName = company, jobRoleName = "Backend Developer",
            deadlineType = "UNSPECIFIED", url = ListingTests.UniqueUrl(), postedOn = "2026-09-20",
            description = $"Runs on {word}", skills = Array.Empty<object>(), languages = Array.Empty<object>(),
            locations = Array.Empty<object>(),
        });

        Assert.Empty(await SearchAsync(client, word, company));
    }

    [Fact]
    public async Task Search_CombinesWithWhere()
    {
        var client = await factory.GetOwnerClientAsync();
        var company = ListingTests.Unique("Acme");
        // Both default to the title "Backend Developer"; only one is applied.
        await ListingTests.ListingWithStatusAsync(client, ListingTests.UniqueUrl(), "APPLIED", company);
        await ListingTests.CreateAsync(client, ListingTests.NewListing(company));
        await ListingTests.CreateAsync(client, ListingTests.NewListing(company, title: "Frontend Developer",
            jobRoleName: "Frontend Developer"));

        var statuses = (await client.GraphQLAsync("""
                query($company: String!) {
                  listings(search: "backend", where: { company: { name: { eq: $company } }, status: { eq: APPLIED } }) {
                    items { status }
                  }
                }
                """, new { company }))
            .GetProperty("listings").GetProperty("items").EnumerateArray()
            .Select(n => n.GetProperty("status").GetString()!).ToList();

        Assert.Equal(["APPLIED"], statuses);
    }

    [Fact]
    public async Task Search_Blank_IsIgnored()
    {
        var client = await factory.GetOwnerClientAsync();
        var company = ListingTests.Unique("Acme");
        await ListingTests.CreateAsync(client, ListingTests.NewListing(company, title: "one"));

        Assert.Equal(["one"], await SearchAsync(client, "  ", company));
    }

    // Scoped to one company because tests share a database.
    private static async Task<List<string>> SearchAsync(HttpClient client, string search, string company) =>
        (await client.GraphQLAsync("""
                query($search: String!, $company: String!) {
                  listings(search: $search, where: { company: { name: { eq: $company } } }) { items { title } }
                }
                """, new { search, company }))
            .GetProperty("listings").GetProperty("items").EnumerateArray()
            .Select(n => n.GetProperty("title").GetString()!).ToList();
}
