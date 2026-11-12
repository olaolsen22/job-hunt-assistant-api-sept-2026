using JobHunt.Api.Data;
using JobHunt.Api.Features.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace JobHunt.Api.Tests.Features.Auth;

public class GuestDataSeederTests : IClassFixture<GuestDataSeederTests.DemoFactory>
{
    private readonly DemoFactory _factory;

    public GuestDataSeederTests(DemoFactory factory) => _factory = factory;

    [Fact]
    public async Task GuestAccount_HasSeededListingsAndProfile()
    {
        var client = await _factory.GetGuestClientAsync();

        var data = await client.GraphQLAsync("""
            {
              listings { totalCount }
              profile { fullName headline }
              baseCvs { name }
              contacts { totalCount }
            }
            """);

        Assert.Equal(9, data.GetProperty("listings").GetProperty("totalCount").GetInt32());
        Assert.Equal("Kari Nordmann", data.GetProperty("profile").GetProperty("fullName").GetString());
        Assert.Equal("Full Stack Developer | .NET & Vue.js", data.GetProperty("profile").GetProperty("headline").GetString());
        Assert.True(data.GetProperty("baseCvs").GetArrayLength() > 0);
        Assert.True(data.GetProperty("contacts").GetProperty("totalCount").GetInt32() >= 3);
    }

    [Fact]
    public async Task GuestAccount_HasVariedListingStatuses()
    {
        var client = await _factory.GetGuestClientAsync();

        var data = await client.GraphQLAsync("""
            {
              listings { items { status } }
            }
            """);

        var statuses = data.GetProperty("listings").GetProperty("items")
            .EnumerateArray().Select(n => n.GetProperty("status").GetString()).ToHashSet();

        Assert.Contains("SAVED", statuses);
        Assert.Contains("APPLIED", statuses);
        Assert.Contains("INTERVIEWING", statuses);
        Assert.Contains("OFFER", statuses);
    }

    [Fact]
    public async Task Idempotent_SecondSeedDoesNotDuplicate()
    {
        await _factory.GetGuestClientAsync();

        using var scope = _factory.Services.CreateScope();
        await GuestDataSeeder.SeedAsync(scope.ServiceProvider);

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var guestId = (await db.Users.SingleAsync(u => u.Email == ApiFactory.GuestEmail)).Id;
        var count = await db.Listings.IgnoreQueryFilters().CountAsync(l => l.UserId == guestId);
        Assert.Equal(10, count);
    }

    [Fact]
    public async Task OwnerData_NotAffectedByGuestSeeding()
    {
        var ownerClient = await _factory.GetOwnerClientAsync();
        await _factory.GetGuestClientAsync();

        var data = await ownerClient.GraphQLAsync("{ listings { totalCount } }");
        Assert.Equal(0, data.GetProperty("listings").GetProperty("totalCount").GetInt32());
    }

    public class DemoFactory : ApiFactory
    {
        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("Seed:GuestDemoData", "true");
        }
    }
}
