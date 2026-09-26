using System.Net;
using System.Net.Http.Json;
using JobHunt.Api.Features.Auth;

namespace JobHunt.Api.Tests.Features.Auth;

public class AuthTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Login_WithOwnerCredentials_ReturnsUserAndSetsCookie()
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest(ApiFactory.OwnerEmail, ApiFactory.OwnerPassword));

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.False(body!.RequiresTwoFactor);
        Assert.Equal(Roles.Owner, body.User!.Role);

        var me = await client.GetFromJsonAsync<UserResponse>("/api/auth/me");
        Assert.Equal(ApiFactory.OwnerEmail, me!.Email);
    }

    [Fact]
    public async Task Login_WithWrongPassword_Returns401()
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest(ApiFactory.OwnerEmail, "wrong"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_WithInvalidEmail_Returns400()
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("not-an-email", "x"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GuestLogin_SignsInAsGuest()
    {
        var client = factory.CreateClient();

        (await client.PostAsync("/api/auth/guest", null)).EnsureSuccessStatusCode();

        var me = await client.GetFromJsonAsync<UserResponse>("/api/auth/me");
        Assert.Equal(Roles.Guest, me!.Role);
        Assert.Equal(ApiFactory.GuestEmail, me.Email);
    }

    [Fact]
    public async Task Me_WhenAnonymous_Returns401()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Logout_ClearsSession()
    {
        var client = factory.CreateClient();
        (await client.PostAsync("/api/auth/guest", null)).EnsureSuccessStatusCode();

        (await client.PostAsync("/api/auth/logout", null)).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }
}

// Own factory: locking the owner out would break the other auth tests.
public class LockoutTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Login_AfterFiveFailures_IsLockedOutEvenWithCorrectPassword()
    {
        var client = factory.CreateClient();
        for (var i = 0; i < 5; i++)
            await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(ApiFactory.OwnerEmail, "wrong"));

        var response = await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest(ApiFactory.OwnerEmail, ApiFactory.OwnerPassword));

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
    }
}
