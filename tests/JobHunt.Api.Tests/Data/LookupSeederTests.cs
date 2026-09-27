namespace JobHunt.Api.Tests.Data;

public class LookupSeederTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task FreshAccount_HasStarterRolesSkillsAndCities()
    {
        var client = await factory.GetOwnerClientAsync();

        var data = await client.GraphQLAsync("""
            {
              jobRoles(search: "Backend") { nodes { name } }
              skills(search: "PostgreSQL") { nodes { name } }
              locations(search: "Tromsø") { nodes { city countryCode } }
            }
            """);

        Assert.Contains("Backend Developer", data.GetProperty("jobRoles").GetProperty("nodes").EnumerateArray().Select(n => n.GetProperty("name").GetString()));
        Assert.Equal("PostgreSQL", data.GetProperty("skills").GetProperty("nodes")[0].GetProperty("name").GetString());
        Assert.Equal("NO", data.GetProperty("locations").GetProperty("nodes")[0].GetProperty("countryCode").GetString());
    }
}
