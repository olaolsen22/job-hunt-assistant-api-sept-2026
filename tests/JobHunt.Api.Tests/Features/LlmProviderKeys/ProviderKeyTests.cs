using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using JobHunt.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace JobHunt.Api.Tests.Features.LlmProviderKeys;

public class ProviderKeyTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Status = "{ providerKeys(includeRetired: true) { provider configured credentialId last4 retiredAt } }";
    internal static Task<JsonElement> SetAsync(HttpClient client, string provider, string key) => client.GraphQLAsync("""
        mutation($provider:String!, $key:String!) {
          setProviderKey(input:{provider:$provider, apiKey:$key}) {
            providerKey { provider configured credentialId last4 } errors { ... on ProviderKeyError { message code } }
          }
        }
        """, new { provider, key });

    internal static Task<JsonElement> RemoveAsync(HttpClient client, string provider) => client.GraphQLAsync("""
        mutation($provider:String!) { removeProviderKey(input:{provider:$provider}) {
          providerKey { provider configured } errors { ... on ProviderKeyError { message code } }
        } }
        """, new { provider });

    [Fact]
    public async Task Keys_ArePrivate_AndSecretsDisappearOnReplacementAndRemoval()
    {
        var owner = await factory.GetOwnerClientAsync();
        var other = await factory.GetSecondOwnerClientAsync();
        var first = (await SetAsync(owner, "anthropic", "private-first-key-1234")).GetProperty("setProviderKey").GetProperty("providerKey");
        var oldId = first.GetProperty("credentialId").GetString();
        Assert.Equal("1234", first.GetProperty("last4").GetString());
        Assert.All((await other.GraphQLAsync(Status)).GetProperty("providerKeys").EnumerateArray(), k => Assert.False(k.GetProperty("configured").GetBoolean()));
        var replacement = (await SetAsync(owner, "anthropic", "private-next-key-5678")).GetProperty("setProviderKey").GetProperty("providerKey");
        Assert.NotEqual(oldId, replacement.GetProperty("credentialId").GetString());
        var statuses = (await owner.GraphQLAsync(Status)).GetProperty("providerKeys");
        Assert.DoesNotContain("private-", statuses.ToString());
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var old = await db.UserProviderKeys.IgnoreQueryFilters().SingleAsync(k => k.Id == Guid.Parse(oldId!));
        Assert.Null(old.ProtectedKey);
        Assert.NotNull(old.RetiredAt);
        await RemoveAsync(owner, "anthropic");
        await RemoveAsync(owner, "anthropic");
        Assert.All(await db.UserProviderKeys.IgnoreQueryFilters().Where(k => k.Provider == "anthropic").ToListAsync(), k => Assert.Null(k.ProtectedKey));
    }

    [Fact]
    public async Task FailedReplacement_PreservesActiveKey_AndGuestIsDeniedBeforeValidation()
    {
        var owner = await factory.GetOwnerClientAsync();
        var guest = await factory.GetGuestClientAsync();
        var first = (await SetAsync(owner, "openai", "working-key-1234")).GetProperty("setProviderKey").GetProperty("providerKey");
        factory.Providers.ValidationStatus = HttpStatusCode.Unauthorized;
        try
        {
            var failed = (await SetAsync(owner, "openai", "rejected-key-5678")).GetProperty("setProviderKey");
            Assert.Equal(JsonValueKind.Null, failed.GetProperty("providerKey").ValueKind);
            Assert.Contains("rejected", failed.GetProperty("errors")[0].GetProperty("message").GetString());
            var active = (await owner.GraphQLAsync(Status)).GetProperty("providerKeys").EnumerateArray()
                .Single(k => k.GetProperty("provider").GetString() == "openai" && k.GetProperty("configured").GetBoolean());
            Assert.Equal(first.GetProperty("credentialId").GetString(), active.GetProperty("credentialId").GetString());
            var before = factory.Providers.ValidationCalls;
            Assert.NotEmpty((await SetAsync(guest, "openai", "guest-key")).GetProperty("setProviderKey").GetProperty("errors").EnumerateArray());
            Assert.NotEmpty((await RemoveAsync(guest, "openai")).GetProperty("removeProviderKey").GetProperty("errors").EnumerateArray());
            Assert.Equal(before, factory.Providers.ValidationCalls);
        }
        finally { factory.Providers.ValidationStatus = HttpStatusCode.OK; await RemoveAsync(owner, "openai"); }
    }

    [Fact]
    public async Task ConcurrentReplacement_LeavesOneActiveKey_AndNoRetiredSecrets()
    {
        var owner = await factory.GetOwnerClientAsync();
        await Task.WhenAll(SetAsync(owner, "mistral", "first-concurrent-key"), SetAsync(owner, "mistral", "second-concurrent-key"));
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var keys = await db.UserProviderKeys.IgnoreQueryFilters().Where(k => k.Provider == "mistral").ToListAsync();
        Assert.Single(keys, k => k.RetiredAt is null);
        Assert.All(keys.Where(k => k.RetiredAt is not null), k => Assert.Null(k.ProtectedKey));
        await RemoveAsync(owner, "mistral");
    }

    [Fact]
    public async Task AnonymousCannotQuery_AndSchemaHasNoStoredSecretFields()
    {
        using var anonymous = factory.CreateClient();
        var response = await anonymous.PostAsJsonAsync("/graphql", new { query = Status });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var owner = await factory.GetOwnerClientAsync();
        var schema = await owner.GraphQLAsync("{ __type(name:\"ProviderKeyStatus\") { fields { name } } }");
        var fields = schema.GetProperty("__type").GetProperty("fields").EnumerateArray().Select(f => f.GetProperty("name").GetString()).ToArray();
        Assert.DoesNotContain("apiKey", fields);
        Assert.DoesNotContain("protectedKey", fields);
        Assert.DoesNotContain("userId", fields);
    }
}
