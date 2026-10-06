using System.Net;
using System.Net.Http.Json;
using JobHunt.Api.Features.Auth;
using JobHunt.Api.Tests.Features.Listings;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

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

// Own factory: the restart test re-seeds with a changed password.
public class SeededUserTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Email = "real@test.local";
    private const string Password = "Real-Passw0rd!";

    private WebApplicationFactory<Program> WithUser(string password) => factory.WithWebHostBuilder(b =>
    {
        b.UseSetting("Seed:Users:0:Email", Email);
        b.UseSetting("Seed:Users:0:Password", password);
    });

    private static async Task<HttpResponseMessage> LoginAsync(HttpClient client, string email, string password) =>
        await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password));

    [Fact]
    public async Task ExtraUser_SignsInAsOwner_AndOwnerStillWorks()
    {
        var app = WithUser(Password);
        var client = app.CreateClient();

        (await LoginAsync(client, Email, Password)).EnsureSuccessStatusCode();
        var me = await client.GetFromJsonAsync<UserResponse>("/api/auth/me");
        Assert.Equal(Email, me!.Email);
        Assert.Equal(Roles.Owner, me.Role);

        (await LoginAsync(app.CreateClient(), ApiFactory.OwnerEmail, ApiFactory.OwnerPassword)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task ExtraUser_DoesNotSeeOwnersListings()
    {
        var app = WithUser(Password);
        var owner = app.CreateClient();
        (await LoginAsync(owner, ApiFactory.OwnerEmail, ApiFactory.OwnerPassword)).EnsureSuccessStatusCode();
        var listingId = (await ListingTests.CreateAsync(owner, ListingTests.NewListing(ListingTests.Unique("Acme"))))
            .GetProperty("id").GetInt32();

        var user = app.CreateClient();
        (await LoginAsync(user, Email, Password)).EnsureSuccessStatusCode();
        var data = await user.GraphQLAsync("{ listings { items { id } } }");

        Assert.DoesNotContain(data.GetProperty("listings").GetProperty("items").EnumerateArray(),
            l => l.GetProperty("id").GetInt32() == listingId);
    }

    [Fact]
    public async Task Restart_KeepsAccountAndData_AndAppliesChangedPassword()
    {
        var before = WithUser(Password).CreateClient();
        (await LoginAsync(before, Email, Password)).EnsureSuccessStatusCode();
        var listingId = (await ListingTests.CreateAsync(before, ListingTests.NewListing(ListingTests.Unique("Acme"))))
            .GetProperty("id").GetInt32();

        var restarted = WithUser("Changed-Passw0rd!");
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(restarted.CreateClient(), Email, Password)).StatusCode);
        var after = restarted.CreateClient();
        (await LoginAsync(after, Email, "Changed-Passw0rd!")).EnsureSuccessStatusCode();

        var data = await after.GraphQLAsync("query ($id: Int!) { listingById(id: $id) { id } }", new { id = listingId });
        Assert.Equal(listingId, data.GetProperty("listingById").GetProperty("id").GetInt32());
    }
}
