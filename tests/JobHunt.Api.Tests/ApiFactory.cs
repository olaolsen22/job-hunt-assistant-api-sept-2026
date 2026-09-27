using System.Net.Http.Json;
using JobHunt.Api.Features.Auth;
using JobHunt.Api.Llm;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace JobHunt.Api.Tests;

// Each factory gets its own named in-memory SQLite DB. It lives as long as one connection
// stays open, so we hold one here for the factory's lifetime.
public class ApiFactory : WebApplicationFactory<Program>
{
    public const string OwnerEmail = "owner@test.local";
    public const string OwnerPassword = "Test-Passw0rd!";
    public const string GuestEmail = "guest@jobhunt.local";

    private readonly string _connectionString = $"Data Source=test-{Guid.NewGuid()};Mode=Memory;Cache=Shared";
    private readonly SqliteConnection _keepAlive;
    private HttpClient? _owner;
    private HttpClient? _guest;

    public FakeChatClient Llm { get; } = new();

    public ApiFactory()
    {
        _keepAlive = new SqliteConnection(_connectionString);
        _keepAlive.Open();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Default", _connectionString);
        builder.UseSetting("Seed:OwnerEmail", OwnerEmail);
        builder.UseSetting("Seed:OwnerPassword", OwnerPassword);
        // CI has no user-secrets; the key is never used because the fake replaces the real client.
        builder.UseSetting("Llm:Gemini:ApiKey", "test-key");
        builder.ConfigureServices(services => services.AddKeyedSingleton<IChatClient>(GeminiOptions.Provider, Llm));
    }

    // Signed in once per factory and reused: the login rate limit (10/min) would trip if every test logged in.
    public async Task<HttpClient> GetOwnerClientAsync()
    {
        if (_owner is not null) return _owner;
        var client = CreateClient();
        (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(OwnerEmail, OwnerPassword))).EnsureSuccessStatusCode();
        return _owner = client;
    }

    public async Task<HttpClient> GetGuestClientAsync()
    {
        if (_guest is not null) return _guest;
        var client = CreateClient();
        (await client.PostAsync("/api/auth/guest", null)).EnsureSuccessStatusCode();
        return _guest = client;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _keepAlive.Dispose();
    }
}
