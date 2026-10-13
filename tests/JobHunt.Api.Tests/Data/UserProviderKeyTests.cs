using System.Security.Claims;
using JobHunt.Api.Data;
using JobHunt.Api.Features.Auth;
using JobHunt.Api.Features.LlmProviderKeys;
using JobHunt.Api.Llm;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace JobHunt.Api.Tests.Data;

public class UserProviderKeyTests : IAsyncLifetime
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private AppDbContext db = null!;
    private readonly HttpContextAccessor http = new();

    public async Task InitializeAsync()
    {
        await connection.OpenAsync();
        db = new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options, http);
        await db.Database.EnsureCreatedAsync();
        db.Users.AddRange(new AppUser { Id = "first", UserName = "first" }, new AppUser { Id = "second", UserName = "second" });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task OwnershipFilter_AndForeignKey_AreEnforced()
    {
        SignIn();
        db.UserProviderKeys.AddRange(Key("first"), Key("second"));
        await db.SaveChangesAsync();
        Assert.Equal("first", (await db.UserProviderKeys.SingleAsync()).UserId);
        Assert.Equal(2, await db.UserProviderKeys.IgnoreQueryFilters().CountAsync());
        db.UserProviderKeys.Add(Key("missing"));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task OnlyOneActiveKey_WhileRetiredHistoryIsAllowed()
    {
        SignIn();
        var retired = Key("first");
        retired.ProtectedKey = null;
        retired.RetiredAt = DateTime.UtcNow;
        db.UserProviderKeys.AddRange(retired, Key("first"));
        await db.SaveChangesAsync();
        Assert.Equal(2, await db.UserProviderKeys.CountAsync());
        db.UserProviderKeys.Add(Key("first"));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task RetiredSecret_CannotBeRetained()
    {
        var key = Key("first");
        key.RetiredAt = DateTime.UtcNow;
        db.UserProviderKeys.Add(key);
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Usage_CannotReferenceAnotherOwnersCredential()
    {
        var key = Key("second");
        db.UserProviderKeys.Add(key);
        await db.SaveChangesAsync();
        db.LlmUsages.Add(new()
        {
            UserId = "first", Provider = "gemini", Model = "test", Operation = "test",
            KeySource = LlmKeySource.User, UserProviderKeyId = key.Id, CreatedAt = DateTime.UtcNow,
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    private static UserProviderKey Key(string owner) => new()
    { UserId = owner, Provider = "gemini", ProtectedKey = "ciphertext", Last4 = "1234", CreatedAt = DateTime.UtcNow };

    private void SignIn() => http.HttpContext = new DefaultHttpContext
    { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "first")], "test")) };

    public async Task DisposeAsync() { await db.DisposeAsync(); await connection.DisposeAsync(); }
}
