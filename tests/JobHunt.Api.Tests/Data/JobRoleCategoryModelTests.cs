using JobHunt.Api.Data;
using JobHunt.Api.Features.JobRoles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace JobHunt.Api.Tests.Data;

public class JobRoleCategoryModelTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Names_AreUniquePerUser_AndCategoryFkIsEnforced()
    {
        await factory.GetOwnerClientAsync();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var categories = await db.JobRoleCategories.IgnoreQueryFilters().Where(c => c.Name == "IT").ToListAsync();
        Assert.Equal(await db.Users.CountAsync(), categories.Count);
        var first = categories[0];
        db.JobRoleCategories.Add(new() { UserId = first.UserId, Name = "it", NormalizedName = "IT" });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        var role = await db.JobRoles.IgnoreQueryFilters().FirstAsync(r => r.UserId == first.UserId);
        role.CategoryId = int.MaxValue;
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        role = await db.JobRoles.IgnoreQueryFilters().FirstAsync(r => r.Id == role.Id);
        role.CategoryId = null;
        await db.SaveChangesAsync();
        Assert.Null(role.CategoryId);
        role.CategoryId = first.Id;
        await db.SaveChangesAsync();
        db.JobRoleCategories.Remove(await db.JobRoleCategories.IgnoreQueryFilters().SingleAsync(c => c.Id == first.Id));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}
