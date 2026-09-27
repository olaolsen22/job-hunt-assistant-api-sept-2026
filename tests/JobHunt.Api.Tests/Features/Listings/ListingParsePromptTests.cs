namespace JobHunt.Api.Tests.Features.Listings;

public class ListingParsePromptTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task ListingParsePrompt_IncludesEnumValuesAndOwnNames()
    {
        var owner = await factory.GetOwnerClientAsync();
        var guest = await factory.GetGuestClientAsync();
        var jobRole = ListingTests.Unique("Platform Engineer");
        var skill = ListingTests.Unique("Rust");
        await ListingTests.CreateAsync(owner, ListingTests.NewListing(ListingTests.Unique("Acme"),
            jobRoleName: jobRole, skills: [new { name = skill }]));

        const string query = "{ listingParsePrompt { version instructions } }";
        var ownerPrompt = (await owner.GraphQLAsync(query)).GetProperty("listingParsePrompt");
        var guestPrompt = (await guest.GraphQLAsync(query)).GetProperty("listingParsePrompt");

        var instructions = ownerPrompt.GetProperty("instructions").GetString()!;
        Assert.Equal(3, ownerPrompt.GetProperty("version").GetInt32());
        Assert.Contains("\"FIXED_TERM\"", instructions);
        Assert.Contains("\"FULL_TIME\"", instructions);
        Assert.Contains("\"C2\"", instructions);
        Assert.Contains(jobRole, instructions);
        Assert.Contains(skill, instructions);
        Assert.DoesNotContain(skill, guestPrompt.GetProperty("instructions").GetString());
    }

    [Fact]
    public async Task ListingQuickParsePrompt_AsksForArrayWithOwnRoles()
    {
        var owner = await factory.GetOwnerClientAsync();
        var jobRole = ListingTests.Unique("Platform Engineer");
        await ListingTests.CreateAsync(owner, ListingTests.NewListing(ListingTests.Unique("Acme"), jobRoleName: jobRole));

        var prompt = (await owner.GraphQLAsync("{ listingQuickParsePrompt { version instructions } }"))
            .GetProperty("listingQuickParsePrompt");

        var instructions = prompt.GetProperty("instructions").GetString()!;
        Assert.Equal(1, prompt.GetProperty("version").GetInt32());
        Assert.Contains("ONE JSON array", instructions);
        Assert.Contains("\"ROLLING\"", instructions);
        Assert.Contains(jobRole, instructions);
    }
}
