using JobHunt.Api.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace JobHunt.Api.Tests.Data;

public class AiSafetyMigrationTests
{
    [Fact]
    public async Task UpgradePreservesByokJobsAndUsage()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options, new HttpContextAccessor());
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261013071810_AddUserProviderKeys");
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO AspNetUsers (Id,UserName,EmailConfirmed,PhoneNumberConfirmed,TwoFactorEnabled,LockoutEnabled,AccessFailedCount)
            VALUES ('u1','u1',0,0,0,0,0);
            INSERT INTO UserProviderKeys (Id,UserId,Provider,ProtectedKey,Last4,CreatedAt)
            VALUES ('00000000-0000-0000-0000-000000000001','u1','openai','ciphertext','1234','2026-10-13');
            INSERT INTO ParseJobs (Id,UserId,Mode,Status,RawText,Provider,CreatedAt)
            VALUES ('00000000-0000-0000-0000-000000000002','u1','Full','Succeeded','posting','gemini','2026-10-13');
            INSERT INTO LlmUsages (UserId,Provider,Model,Operation,InputTokens,OutputTokens,LatencyMs,Success,CreatedAt,KeySource,UserProviderKeyId)
            VALUES ('u1','openai','test','parse-listing',1200,300,42,1,'2026-10-13','User','00000000-0000-0000-0000-000000000001');
            """);
        await migrator.MigrateAsync();
        Assert.Empty(await db.AiOperations.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(await db.UserLlmLimits.IgnoreQueryFilters().ToListAsync());
        Assert.Equal("ciphertext", (await db.UserProviderKeys.IgnoreQueryFilters().SingleAsync()).ProtectedKey);
        Assert.Equal("posting", (await db.ParseJobs.IgnoreQueryFilters().SingleAsync()).RawText);
        var usage = await db.LlmUsages.IgnoreQueryFilters().SingleAsync();
        Assert.Equal(1200, usage.InputTokens); Assert.Equal(300, usage.OutputTokens);
        Assert.Null(usage.AiOperationId); Assert.Null(usage.AttemptOrdinal);
        await using var command = connection.CreateCommand(); command.CommandText = "PRAGMA foreign_key_check";
        await using var reader = await command.ExecuteReaderAsync(); Assert.False(await reader.ReadAsync());
    }
}
