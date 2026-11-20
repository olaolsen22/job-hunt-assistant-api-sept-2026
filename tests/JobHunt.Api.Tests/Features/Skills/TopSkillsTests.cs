using System.Text.Json;

namespace JobHunt.Api.Tests.Features.Skills;

public class TopSkillsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static async Task CreateListingAsync(HttpClient client, params (string Name, bool Required)[] skills) =>
        await client.GraphQLAsync("""
            mutation ($listing: ListingInput!) { createListing(input: { listing: $listing }) { listing { id } } }
            """, new
        {
            listing = new
            {
                title = "Developer", companyName = "Top skills company", jobRoleName = "Developer",
                url = $"https://top-skills.example/{Guid.NewGuid()}", postedOn = "2026-10-01",
                deadlineType = "UNSPECIFIED",
                skills = skills.Select(s => new { name = s.Name, level = 3, isRequired = s.Required }).ToArray(),
                languages = Array.Empty<object>(), locations = Array.Empty<object>(),
            },
        });

    private static async Task<Dictionary<string, int>> TopAsync(HttpClient client, bool requiredOnly = false, string? since = null) =>
        (await client.GraphQLAsync("""
            query ($requiredOnly: Boolean!, $since: DateTime) {
              topSkills(limit: 100, requiredOnly: $requiredOnly, since: $since) { listingCount skill { name } }
            }
            """, new { requiredOnly, since }))
        .GetProperty("topSkills").EnumerateArray()
        .ToDictionary(e => e.GetProperty("skill").GetProperty("name").GetString()!, e => e.GetProperty("listingCount").GetInt32());

    [Fact]
    public async Task TopSkills_CountsListingsPerSkill_OrderedAndFiltered_PerUser()
    {
        var owner = await factory.GetOwnerClientAsync();
        await CreateListingAsync(owner, ("TopRust", true), ("TopGo", false));
        await CreateListingAsync(owner, ("TopRust", true), ("TopGo", true));
        await CreateListingAsync(owner, ("TopRust", false));

        var all = await TopAsync(owner);
        Assert.Equal(3, all["TopRust"]);
        Assert.Equal(2, all["TopGo"]);
        var json = await owner.GraphQLAsync("{ topSkills(limit: 100) { listingCount } }");
        var ordered = json.GetProperty("topSkills").EnumerateArray().Select(e => e.GetProperty("listingCount").GetInt32()).ToList();
        Assert.Equal(ordered.OrderDescending(), ordered);

        var required = await TopAsync(owner, requiredOnly: true);
        Assert.Equal(2, required["TopRust"]);
        Assert.Equal(1, required["TopGo"]);

        Assert.DoesNotContain("TopRust", (await TopAsync(owner, since: "2100-01-01T00:00:00Z")).Keys);

        var other = await factory.GetSecondOwnerClientAsync();
        Assert.DoesNotContain("TopRust", (await TopAsync(other)).Keys);
    }
}
