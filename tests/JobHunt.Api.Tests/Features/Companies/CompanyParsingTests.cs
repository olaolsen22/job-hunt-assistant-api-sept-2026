using System.Text.Json;
using JobHunt.Api.Data;
using JobHunt.Api.Features.Parsing;
using JobHunt.Api.Tests.Features.Listings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace JobHunt.Api.Tests.Features.Companies;

public class CompanyParsingTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string ParseListing = """
        mutation ($text: String!, $mode: ParseMode!) {
          parseListing(input: { text: $text, mode: $mode }) {
            parseJob { status result error }
            errors { __typename }
          }
        }
        """;

    private const string Prompt = "{ companyParsePrompt { version instructions } }";

    private static ChatResponse Answer(string text) =>
        new(new ChatMessage(ChatRole.Assistant, text)) { Usage = new UsageDetails { InputTokenCount = 100, OutputTokenCount = 50 } };

    [Fact]
    public async Task CompanyParsePrompt_ListsOwnIndustriesAndAsksForArray()
    {
        var owner = await factory.GetOwnerClientAsync();
        var guest = await factory.GetGuestClientAsync();
        var industry = ListingTests.Unique("Offshore Wind");
        var name = ListingTests.Unique("Acme");
        var listing = await ListingTests.CreateAsync(owner, ListingTests.NewListing(name));
        await owner.GraphQLAsync("""
            mutation ($id: Int!, $name: String!, $industry: String!) {
              updateCompany(input: { id: $id, name: $name, industryName: $industry, locations: [] }) { company { id } }
            }
            """, new { id = listing.GetProperty("company").GetProperty("id").GetInt32(), name, industry });

        var ownerPrompt = (await owner.GraphQLAsync(Prompt)).GetProperty("companyParsePrompt");
        var guestPrompt = (await guest.GraphQLAsync(Prompt)).GetProperty("companyParsePrompt");

        var instructions = ownerPrompt.GetProperty("instructions").GetString()!;
        Assert.Equal(4, ownerPrompt.GetProperty("version").GetInt32());
        Assert.Contains("\"RECRUITMENT\" | \"BROKER\"", instructions);
        Assert.Contains("RECRUITMENT is a recruitment or", instructions);
        Assert.Contains("BROKER matches", instructions);
        Assert.Contains("ONE JSON array", instructions);
        Assert.Contains("<posting>", instructions);
        Assert.Contains("ISO 3166-1", instructions);
        Assert.Contains(industry, instructions);
        Assert.Contains("\"orgNumber\"", instructions);
        Assert.Contains("\"PRODUCT\" | \"CONSULTANCY\" | \"IN_HOUSE\"", instructions);
        Assert.Contains("\"skills\": string[]", instructions);
        Assert.Contains("\"contacts\": [{ \"name\": string", instructions);
        Assert.Contains("never build an email from a pattern", instructions);
        Assert.Contains("company's own site and LinkedIn pages you open", instructions);
        Assert.DoesNotContain(industry, guestPrompt.GetProperty("instructions").GetString());
    }

    [Fact]
    public async Task ParseListing_Companies_ReturnsRowsWithoutSavingAnything_AndLogsItsOwnOperation()
    {
        var client = await factory.GetOwnerClientAsync();
        var name = ListingTests.Unique("Parsed Co");
        var rows = $$"""[{ "name": "{{name}}", "industryName": "Energy", "locations": [{ "city": "Oslo", "countryCode": "NO" }] }]""";
        factory.Llm.Respond = () => Answer(rows);

        var job = (await client.GraphQLAsync(ParseListing, new { text = "Equinor, Aker BP", mode = "COMPANIES" }))
            .GetProperty("parseListing").GetProperty("parseJob");

        Assert.Equal("SUCCEEDED", job.GetProperty("status").GetString());
        Assert.Equal(rows, job.GetProperty("result").GetString());
        Assert.Equal("<posting>\nEquinor, Aker BP\n</posting>", factory.Llm.LastMessages[1].Text);

        var prompt = (await client.GraphQLAsync(Prompt)).GetProperty("companyParsePrompt");
        Assert.Equal(prompt.GetProperty("instructions").GetString()!
            .Replace(JsonReply.Rule("array", external: true), JsonReply.Rule("array", external: false))
            .Replace(" or on the company's own site and LinkedIn pages you open", ""), factory.Llm.LastMessages[0].Text);

        var companies = await client.GraphQLAsync(
            "query ($s: String) { companies(search: $s) { totalCount } }", new { s = name });
        Assert.Equal(0, companies.GetProperty("companies").GetProperty("totalCount").GetInt32());

        using var scope = factory.Services.CreateScope();
        var usage = await scope.ServiceProvider.GetRequiredService<AppDbContext>().LlmUsages
            .IgnoreQueryFilters().OrderByDescending(u => u.Id).FirstAsync();
        Assert.Equal("parse-companies", usage.Operation);
        Assert.True(usage.Success);
    }

    [Fact]
    public async Task ParseListing_CompaniesGettingObject_Fails()
    {
        var client = await factory.GetOwnerClientAsync();
        factory.Llm.Respond = () => Answer("""{ "name": "Not an array" }""");

        var job = (await client.GraphQLAsync(ParseListing, new { text = "Equinor", mode = "COMPANIES" }))
            .GetProperty("parseListing").GetProperty("parseJob");

        Assert.Equal("FAILED", job.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, job.GetProperty("result").ValueKind);
        Assert.Contains("couldn't be read", job.GetProperty("error").GetString());
    }

    [Fact]
    public async Task ParseListing_CompaniesWithBlankText_ReturnsInvalidParseRequestError()
    {
        var client = await factory.GetOwnerClientAsync();

        var data = await client.GraphQLAsync(ParseListing, new { text = "   ", mode = "COMPANIES" });

        Assert.Equal("InvalidParseRequestError",
            data.GetProperty("parseListing").GetProperty("errors")[0].GetProperty("__typename").GetString());
    }
}
