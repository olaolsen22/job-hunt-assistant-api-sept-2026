using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;

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
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _keepAlive.Dispose();
    }
}
