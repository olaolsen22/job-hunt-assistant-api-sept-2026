using System.Text.Json;
using JobHunt.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static JobHunt.Api.Tests.Features.Listings.ListingTests;

namespace JobHunt.Api.Tests.Features.Listings;

public class ListingTrainingProgramTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string ProgramFields = """
        id company { id name }
        trainingProgram { startsOn endsOn isPaid jobOffer endCompany { id name } }
        """;

    private const string Create = $$"""
        mutation ($listing: ListingInput!) {
          createListing(input: { listing: $listing }) {
            listing { {{ProgramFields}} }
            errors { __typename ... on Error { message } }
          }
        }
        """;

    private const string Update = $$"""
        mutation ($id: Int!, $listing: ListingInput!) {
          updateListing(input: { id: $id, listing: $listing }) {
            listing { {{ProgramFields}} }
            errors { __typename }
          }
        }
        """;

    [Fact]
    public async Task CreateListing_WithProgram_SavesItWithEndCompany()
    {
        var client = await factory.GetOwnerClientAsync();
        var provider = Unique("AW Academy");
        var employer = Unique("Kongsberg Defence Communication");

        var listing = await CreateAsync(client, WithProgram(provider, new
        {
            endCompanyName = employer, startsOn = "2027-01-04", endsOn = "2027-04-02", isPaid = false, jobOffer = "ON_PASS",
        }));

        Assert.Equal(provider, listing.GetProperty("company").GetProperty("name").GetString());
        var program = listing.GetProperty("trainingProgram");
        Assert.Equal(employer, program.GetProperty("endCompany").GetProperty("name").GetString());
        Assert.Equal("2027-01-04", program.GetProperty("startsOn").GetString());
        Assert.Equal("2027-04-02", program.GetProperty("endsOn").GetString());
        Assert.False(program.GetProperty("isPaid").GetBoolean());
        Assert.Equal("ON_PASS", program.GetProperty("jobOffer").GetString());
    }

    [Fact]
    public async Task CreateListing_WithoutProgram_HasNone()
    {
        var client = await factory.GetOwnerClientAsync();

        var listing = await CreateAsync(client, NewListing(Unique("Acme")));

        Assert.Equal(JsonValueKind.Null, listing.GetProperty("trainingProgram").ValueKind);
    }

    [Fact]
    public async Task CreateListing_WithUnnamedEndEmployer_SavesProgramWithoutEndCompany()
    {
        var client = await factory.GetOwnerClientAsync();

        var listing = await CreateAsync(client, WithProgram(Unique("AW Academy"), new { endCompanyName = (string?)null }));

        var program = listing.GetProperty("trainingProgram");
        Assert.Equal(JsonValueKind.Null, program.GetProperty("endCompany").ValueKind);
        Assert.Equal(JsonValueKind.Null, program.GetProperty("isPaid").ValueKind);
        Assert.Equal("UNSPECIFIED", program.GetProperty("jobOffer").GetString());
    }

    [Fact]
    public async Task CreateListing_ReusesExistingEndCompanyAndProviderName()
    {
        var client = await factory.GetOwnerClientAsync();
        var employer = Unique("Kongsberg");
        var existing = await CreateAsync(client, NewListing(employer));

        var listing = await CreateAsync(client, WithProgram(employer.ToUpperInvariant(), new { endCompanyName = $" {employer} ".ToLowerInvariant() }));

        var existingCompanyId = existing.GetProperty("company").GetProperty("id").GetInt32();
        Assert.Equal(existingCompanyId, listing.GetProperty("company").GetProperty("id").GetInt32());
        Assert.Equal(existingCompanyId,
            listing.GetProperty("trainingProgram").GetProperty("endCompany").GetProperty("id").GetInt32());
    }

    [Fact]
    public async Task UpdateListing_OmittedKeepsProgram_NullRemovesIt()
    {
        var client = await factory.GetOwnerClientAsync();
        var provider = Unique("AW Academy");
        var id = (await CreateAsync(client, WithProgram(provider, new { jobOffer = "GUARANTEED" }))).GetProperty("id").GetInt32();

        var kept = await UpdateAsync(client, id, NewListing(provider));
        Assert.Equal("GUARANTEED", kept.GetProperty("trainingProgram").GetProperty("jobOffer").GetString());

        var removed = await UpdateAsync(client, id, WithProgram(provider, null));
        Assert.Equal(JsonValueKind.Null, removed.GetProperty("trainingProgram").ValueKind);
    }

    [Fact]
    public async Task UpdateListing_ReplacesProgram()
    {
        var client = await factory.GetOwnerClientAsync();
        var provider = Unique("AW Academy");
        var id = (await CreateAsync(client, WithProgram(provider, new { endCompanyName = Unique("Old"), isPaid = true })))
            .GetProperty("id").GetInt32();

        var updated = await UpdateAsync(client, id, WithProgram(provider, new { jobOffer = "NONE" }));

        var program = updated.GetProperty("trainingProgram");
        Assert.Equal(JsonValueKind.Null, program.GetProperty("endCompany").ValueKind);
        Assert.Equal(JsonValueKind.Null, program.GetProperty("isPaid").ValueKind);
        Assert.Equal("NONE", program.GetProperty("jobOffer").GetString());
    }

    [Fact]
    public async Task CreateListing_WithProgramEndingBeforeStart_ReturnsInvalidListingError()
    {
        var client = await factory.GetOwnerClientAsync();

        var data = await client.GraphQLAsync(Create, new
        {
            listing = WithProgram(Unique("AW Academy"), new { startsOn = "2027-04-02", endsOn = "2027-01-04" }),
        });

        var payload = data.GetProperty("createListing");
        Assert.Equal(JsonValueKind.Null, payload.GetProperty("listing").ValueKind);
        Assert.Equal("InvalidListingError", payload.GetProperty("errors")[0].GetProperty("__typename").GetString());
    }

    [Fact]
    public async Task Listings_FilterByHavingProgram()
    {
        var client = await factory.GetOwnerClientAsync();
        var programId = (await CreateAsync(client, WithProgram(Unique("AW Academy"), new { }))).GetProperty("id").GetInt32();
        var jobId = (await CreateAsync(client, NewListing(Unique("Acme")))).GetProperty("id").GetInt32();

        // Scoped to the two ids: tests share one DB, so other listings would push these off the page.
        var data = await client.GraphQLAsync("""
            query ($ids: [Int!]!) {
              programs: listings(isTrainingProgram: true, where: { id: { in: $ids } }) { items { id } }
              jobs: listings(isTrainingProgram: false, where: { id: { in: $ids } }) { items { id } }
            }
            """, new { ids = new[] { programId, jobId } });

        var programs = Ids(data, "programs");
        var jobs = Ids(data, "jobs");
        Assert.Contains(programId, programs);
        Assert.DoesNotContain(jobId, programs);
        Assert.Contains(jobId, jobs);
        Assert.DoesNotContain(programId, jobs);

        static int[] Ids(JsonElement data, string list) =>
            data.GetProperty(list).GetProperty("items").EnumerateArray().Select(n => n.GetProperty("id").GetInt32()).ToArray();
    }

    [Fact]
    public async Task Program_IsInvisibleToOtherUsersAndDeletedWithListing()
    {
        var owner = await factory.GetOwnerClientAsync();
        var guest = await factory.GetGuestClientAsync();
        var id = (await CreateAsync(owner, WithProgram(Unique("AW Academy"), new { }))).GetProperty("id").GetInt32();

        var seen = await guest.GraphQLAsync("""
            query ($id: Int!) { listings(isTrainingProgram: true, where: { id: { eq: $id } }) { items { id } } }
            """, new { id });
        Assert.DoesNotContain(seen.GetProperty("listings").GetProperty("items").EnumerateArray(),
            n => n.GetProperty("id").GetInt32() == id);

        await owner.GraphQLAsync("mutation ($id: Int!) { deleteListing(input: { id: $id }) { id } }", new { id });

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.TrainingPrograms.IgnoreQueryFilters().AnyAsync(t => t.ListingId == id));
    }

    // NewListing plus a trainingProgram field; null sends an explicit null, which removes the program.
    private static Dictionary<string, object?> WithProgram(string companyName, object? trainingProgram)
    {
        var listing = JsonSerializer.SerializeToElement(NewListing(companyName))
            .Deserialize<Dictionary<string, object?>>()!;
        listing["trainingProgram"] = trainingProgram;
        return listing;
    }

    private static async Task<JsonElement> CreateAsync(HttpClient client, object listing)
    {
        var data = await client.GraphQLAsync(Create, new { listing });
        return data.GetProperty("createListing").GetProperty("listing");
    }

    private static async Task<JsonElement> UpdateAsync(HttpClient client, int id, object listing)
    {
        var data = await client.GraphQLAsync(Update, new { id, listing });
        return data.GetProperty("updateListing").GetProperty("listing");
    }
}
