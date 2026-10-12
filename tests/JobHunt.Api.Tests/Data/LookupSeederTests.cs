using JobHunt.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

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

    [Fact]
    public async Task EveryUser_HasExactlyOneProfile_AndReseedingAddsNone()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await LookupSeeder.SeedAsync(db);

        var userIds = await db.Users.Select(u => u.Id).ToListAsync();
        var profileUserIds = await db.Profiles.IgnoreQueryFilters().Select(p => p.UserId).ToListAsync();
        Assert.Equal(userIds.Order(), profileUserIds.Order());
    }

    [Fact]
    public async Task Categories_SeedTechnicalRoles_AndPreserveCorrectionsAndClears()
    {
        await factory.GetOwnerClientAsync();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var users = await db.Users.Select(u => u.Id).ToListAsync();
        foreach (var userId in users)
        {
            var categories = await db.JobRoleCategories.IgnoreQueryFilters().Where(c => c.UserId == userId).ToListAsync();
            Assert.Equal(8, categories.Count);
            var backend = await db.JobRoles.IgnoreQueryFilters().SingleAsync(r => r.UserId == userId && r.Name == "Backend Developer");
            var frontend = await db.JobRoles.IgnoreQueryFilters().SingleAsync(r => r.UserId == userId && r.Name == "Frontend Developer");
            Assert.Equal(categories.Single(c => c.Name == "IT").Id, backend.CategoryId);
            backend.CategoryId = categories.Single(c => c.Name == "Healthcare").Id;
            frontend.CategoryId = null;
            var manager = await db.JobRoles.IgnoreQueryFilters().SingleAsync(r => r.UserId == userId && r.Name == "Engineering Manager");
            Assert.Null(manager.CategoryId);
        }
        await db.SaveChangesAsync();
        await LookupSeeder.SeedAsync(db);
        db.ChangeTracker.Clear();
        foreach (var userId in users)
        {
            var backend = await db.JobRoles.IgnoreQueryFilters().Include(r => r.Category)
                .SingleAsync(r => r.UserId == userId && r.Name == "Backend Developer");
            Assert.Equal("Healthcare", backend.Category!.Name);
            Assert.Null((await db.JobRoles.IgnoreQueryFilters().SingleAsync(r => r.UserId == userId && r.Name == "Frontend Developer")).CategoryId);
            Assert.Equal(8, await db.JobRoleCategories.IgnoreQueryFilters().CountAsync(c => c.UserId == userId));
        }
    }
}
