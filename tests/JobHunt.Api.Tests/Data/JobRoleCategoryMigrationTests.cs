using JobHunt.Api.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace JobHunt.Api.Tests.Data;

public class JobRoleCategoryMigrationTests
{
    [Fact]
    public async Task Upgrade_BackfillsPerUser_AndPreservesListingReferences()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options,
            new HttpContextAccessor());
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261009170523_AddTrainingPrograms");
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO AspNetUsers (Id, UserName, NormalizedUserName, EmailConfirmed, PhoneNumberConfirmed, TwoFactorEnabled, LockoutEnabled, AccessFailedCount)
            VALUES ('u1','u1','U1',0,0,0,0,0), ('u2','u2','U2',0,0,0,0,0);
            INSERT INTO JobRoles (Id, UserId, Name, NormalizedName)
            VALUES (101,'u1','Backend Developer','BACKEND DEVELOPER'),
                   (102,'u1','Warehouse Worker','WAREHOUSE WORKER'),
                   (103,'u2','Backend Developer','BACKEND DEVELOPER'),
                   (104,'u1','Product Manager','PRODUCT MANAGER');
            INSERT INTO Companies (Id,UserId,Name,NormalizedName) VALUES (101,'u1','Bank','BANK');
            INSERT INTO Listings (Id,UserId,Title,NormalizedTitle,CompanyId,JobRoleId,Url,PostedOn,DeadlineType,CreatedAt,UpdatedAt,Status)
            VALUES (101,'u1','Developer','DEVELOPER',101,101,'https://example.com/101','2026-10-01','Unspecified','2026-10-01','2026-10-01','Saved');
            """);
        await migrator.MigrateAsync();
        var roles = await db.JobRoles.IgnoreQueryFilters().Include(r => r.Category).OrderBy(r => r.Id).ToListAsync();
        Assert.Equal(new[] { 101, 102, 103, 104 }, roles.Select(r => r.Id));
        Assert.Equal("IT", roles[0].Category!.Name);
        Assert.Equal("u1", roles[0].Category!.UserId);
        Assert.Null(roles[1].CategoryId);
        Assert.Equal("u2", roles[2].Category!.UserId);
        Assert.NotEqual(roles[0].CategoryId, roles[2].CategoryId);
        Assert.Null(roles[3].CategoryId);
        Assert.Equal(16, await db.JobRoleCategories.IgnoreQueryFilters().CountAsync());
        var listing = await db.Listings.IgnoreQueryFilters().SingleAsync();
        Assert.Equal(101, listing.Id);
        Assert.Equal(101, listing.JobRoleId);
        Assert.Equal("https://example.com/101", listing.Url);
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_key_check";
        await using var reader = await command.ExecuteReaderAsync();
        Assert.False(await reader.ReadAsync());
    }
}
