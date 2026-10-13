using JobHunt.Api.Data;
using JobHunt.Api.Llm;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace JobHunt.Api.Tests.Data;

public class UserProviderKeyMigrationTests
{
    [Fact]
    public async Task Upgrade_PreservesUsage_AsSharedHistory()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options,
            new HttpContextAccessor());
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261013052327_AddLlmModelSelection");
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO AspNetUsers (Id, UserName, EmailConfirmed, PhoneNumberConfirmed, TwoFactorEnabled, LockoutEnabled, AccessFailedCount)
            VALUES ('u1','u1',0,0,0,0,0);
            INSERT INTO LlmUsages (UserId,Provider,Model,Operation,InputTokens,OutputTokens,LatencyMs,Success,CreatedAt)
            VALUES ('u1','gemini','old-model','parse-listing',1200,300,42,1,'2026-10-01');
            """);
        await migrator.MigrateAsync();
        var row = await db.LlmUsages.IgnoreQueryFilters().SingleAsync();
        Assert.Equal(LlmKeySource.Shared, row.KeySource);
        Assert.Null(row.UserProviderKeyId);
        Assert.Equal("old-model", row.Model);
        Assert.Equal(1200, row.InputTokens);
        Assert.Equal(300, row.OutputTokens);
        Assert.True(row.Success);
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_key_check";
        await using var reader = await command.ExecuteReaderAsync();
        Assert.False(await reader.ReadAsync());
    }
}
