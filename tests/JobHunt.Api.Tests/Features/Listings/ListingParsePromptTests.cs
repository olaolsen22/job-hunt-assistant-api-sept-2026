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
        Assert.Equal(14, ownerPrompt.GetProperty("version").GetInt32());
        Assert.Contains("\"kind\": \"VACANCY\" | \"OPEN_INVITATION\"", instructions);
        Assert.Contains("åpen søknad", instructions);
        Assert.Contains("\"trainingProgram\"", instructions);
        Assert.Contains("\"ON_PASS\"", instructions);
        Assert.Contains("partner companies", instructions);
        Assert.Contains("never INTERNSHIP", instructions);
        Assert.DoesNotContain("Trainee-program", instructions);
        Assert.Contains("\"FIXED_TERM\"", instructions);
        Assert.Contains("\"FULL_TIME\"", instructions);
        Assert.Contains("\"C2\"", instructions);
        Assert.Contains("\"Løpende\"/\"fortløpende\"/\"rolling\" → ASAP", instructions);
        Assert.DoesNotContain("ROLLING", instructions);
        Assert.Contains("Stillingstittel", instructions);
        Assert.Contains("short, direct job title in natural English", instructions);
        Assert.DoesNotContain("original language", instructions);
        Assert.Contains("\"contacts\"", instructions);
        Assert.Contains("Kontaktperson", instructions);
        Assert.Contains("erfaren", instructions);
        Assert.Contains("hjemmekontor", instructions, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Sist endret", instructions);
        Assert.Contains("i dag", instructions);
        Assert.Contains("only names that appear in the posting", instructions);
        Assert.Contains("Grafana, not \"Monitoring\"", instructions);
        Assert.DoesNotContain("Don't guess", instructions);
        Assert.DoesNotContain("Not from years alone", instructions);
        Assert.Contains(jobRole, instructions);
        Assert.Contains(skill, instructions);
        Assert.DoesNotContain(skill, guestPrompt.GetProperty("instructions").GetString());
    }

    [Fact]
    public async Task Categories_ArePrivate_IncludeAssignments_AndHaveUncertaintyRules()
    {
        var owner = await factory.GetOwnerClientAsync();
        var guest = await factory.GetGuestClientAsync();
        var name = ListingTests.Unique("Private category");
        var categoryId = await JobRoles.JobRoleCategoryTests.CategoryAsync(owner, name);
        var roleName = ListingTests.Unique("Private role");
        var listing = await ListingTests.CreateAsync(owner, ListingTests.NewListing(ListingTests.Unique("Acme"), jobRoleName: roleName));
        await owner.GraphQLAsync(JobRoles.JobRoleCategoryTests.AssignCategory,
            new { jobRoleId = listing.GetProperty("jobRole").GetProperty("id").GetInt32(), categoryId });
        foreach (var field in new[] { "listingParsePrompt", "listingQuickParsePrompt" })
        {
            var instructions = (await owner.GraphQLAsync("{ " + field + " { instructions } }"))
                .GetProperty(field).GetProperty("instructions").GetString()!;
            Assert.Contains(name, instructions);
            Assert.Contains(roleName, instructions);
            Assert.Contains("Known role assignments", instructions);
            Assert.Contains("jobRoleCategoryName", instructions);
            Assert.Contains("Never invent category names", instructions);
            Assert.Contains("When uncertain or no existing category fits, return null", instructions);
            Assert.Contains("never solely from company industry", instructions);
            var foreign = (await guest.GraphQLAsync("{ " + field + " { instructions } }"))
                .GetProperty(field).GetProperty("instructions").GetString()!;
            Assert.DoesNotContain(name, foreign);
            Assert.DoesNotContain(roleName, foreign);
        }
        var companies = (await owner.GraphQLAsync("{companyParsePrompt{version instructions}}"))
            .GetProperty("companyParsePrompt");
        Assert.Equal(2, companies.GetProperty("version").GetInt32());
        Assert.DoesNotContain("jobRoleCategoryName", companies.GetProperty("instructions").GetString());
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
        Assert.Equal(8, prompt.GetProperty("version").GetInt32());
        Assert.Contains("\"kind\": \"VACANCY\" | \"OPEN_INVITATION\"", instructions);
        Assert.Contains("\"postingLanguage\": \"ENGLISH\" | \"NORWEGIAN\" | null", instructions);
        Assert.Contains("skandinavisk språk", instructions);
        Assert.DoesNotContain("contacts", instructions);
        Assert.DoesNotContain("trainingProgram", instructions);
        Assert.Contains("ONE JSON array", instructions);
        Assert.Contains("Stillingstittel", instructions);
        Assert.Contains("short, direct job title in natural English", instructions);
        Assert.DoesNotContain("original language", instructions);
        Assert.Contains("i dag", instructions);
        Assert.DoesNotContain("Don't guess", instructions);
        Assert.Contains("\"Løpende\"/\"fortløpende\"/\"rolling\" → ASAP", instructions);
        Assert.DoesNotContain("ROLLING", instructions);
        Assert.Contains(jobRole, instructions);
    }
}
