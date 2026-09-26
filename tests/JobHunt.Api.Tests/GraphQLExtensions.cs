using System.Net.Http.Json;
using System.Text.Json;

namespace JobHunt.Api.Tests;

public static class GraphQLExtensions
{
    // Returns "data". Top-level errors mean a broken query or a server bug, so they fail the test;
    // expected failures come back as typed errors inside the mutation payload instead.
    public static async Task<JsonElement> GraphQLAsync(this HttpClient client, string query, object? variables = null)
    {
        var response = await client.PostAsJsonAsync("/graphql", new { query, variables });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        if (body.TryGetProperty("errors", out var errors))
            Assert.Fail(errors.ToString());
        response.EnsureSuccessStatusCode();
        return body.GetProperty("data");
    }
}
