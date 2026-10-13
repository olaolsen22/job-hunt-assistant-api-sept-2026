using System.Net.Http.Json;
using JobHunt.Api.Features.Auth;
using JobHunt.Api.Features.Parsing;
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
    // The model in appsettings.json, and the extra one added below.
    public const string DefaultModel = "gemini-3.1-flash-lite";
    public const string StrongModel = "test-strong-model";

    private readonly string _connectionString = $"Data Source=test-{Guid.NewGuid()};Mode=Memory;Cache=Shared";
    private readonly SqliteConnection _keepAlive;
    private HttpClient? _owner;
    private HttpClient? _guest;

    public FakeChatClient Llm { get; } = new();
    public FakePageHandler Pages { get; } = new();

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
        // A second allowed model next to the appsettings.json default, so the Owner has something to pick.
        builder.UseSetting("Llm:Gemini:Models:1:Id", StrongModel);
        builder.UseSetting("Llm:Gemini:Models:1:Label", "Strong");
        builder.UseSetting("Llm:Gemini:Models:1:RequestsPerDay", "250");
        // A model with prices, so usageDaily's estimated cost has something to compute; the configured model has none.
        builder.UseSetting("Llm:Pricing:priced-model:InputPerMTok", "0.10");
        builder.UseSetting("Llm:Pricing:priced-model:OutputPerMTok", "0.40");
        builder.ConfigureServices(services =>
        {
            services.AddKeyedSingleton<IChatClient>(GeminiOptions.Provider, Llm);
            // The last primary handler registered wins, so listing pages come from the fake, never the network.
            services.AddHttpClient<ListingPageFetcher>().ConfigurePrimaryHttpMessageHandler(() => Pages);
        });
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
