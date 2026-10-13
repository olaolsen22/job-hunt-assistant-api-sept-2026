using System.Net.Http.Json;
using System.Text.Json;
using JobHunt.Api.Data;
using JobHunt.Api.Features.Usage;
using JobHunt.Api.Llm;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace JobHunt.Api.Tests.Features.Usage;

public class UsageTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    // The configured default; llmUsageToday counts only the caller's effective default model.
    private const string Configured = ApiFactory.DefaultModel;

    private const string Today = "{ llmUsageToday { model requests inputTokens outputTokens requestsLimit remainingRequests resetsAt } }";
    private const string Daily = "query($days: Int) { usageDaily(days: $days) { day model requests inputTokens outputTokens estimatedCost } }";

    // Rows are dated relative to the real clock, so a run within seconds of midnight Pacific time could flip a day.
    private static DateTime TodayStart => PacificDay.StartUtc(PacificDay.Of(DateTime.UtcNow));

    [Fact]
    public async Task Today_SumsEveryUsersCalls_AndCountsFailedCallsWithNoTokens()
    {
        var owner = await factory.GetOwnerClientAsync();
        var guest = await factory.GetGuestClientAsync();
        await ResetAsync(
            Row(await UserIdAsync(owner), Configured, DateTime.UtcNow, 100, 50),
            Row(await UserIdAsync(owner), Configured, DateTime.UtcNow, 100, 50),
            Row(await UserIdAsync(guest), Configured, DateTime.UtcNow, null, null, success: false));

        var usage = (await guest.GraphQLAsync(Today)).GetProperty("llmUsageToday");

        Assert.Equal(3, usage.GetProperty("requests").GetInt32());
        Assert.Equal(200, usage.GetProperty("inputTokens").GetInt64());
        Assert.Equal(100, usage.GetProperty("outputTokens").GetInt64());
    }

    [Fact]
    public async Task Today_LeavesOutCallsBeforeMidnightPacific_AndOtherModels()
    {
        var owner = await factory.GetOwnerClientAsync();
        var userId = await UserIdAsync(owner);
        await ResetAsync(
            Row(userId, Configured, TodayStart.AddMinutes(-1), 100, 50),
            Row(userId, "other-model", DateTime.UtcNow, 100, 50),
            Row(userId, Configured, DateTime.UtcNow, 7, 3));

        var usage = (await owner.GraphQLAsync(Today)).GetProperty("llmUsageToday");

        Assert.Equal(1, usage.GetProperty("requests").GetInt32());
        Assert.Equal(7, usage.GetProperty("inputTokens").GetInt64());
    }

    [Fact]
    public async Task Today_ResetsAtTheNextMidnightPacific()
    {
        var owner = await factory.GetOwnerClientAsync();
        await ResetAsync();

        var usage = (await owner.GraphQLAsync(Today)).GetProperty("llmUsageToday");

        var next = PacificDay.StartUtc(PacificDay.Of(DateTime.UtcNow).AddDays(1));
        Assert.Equal(next, usage.GetProperty("resetsAt").GetDateTime().ToUniversalTime());
    }

    [Fact]
    public async Task Today_ReportsTheConfiguredLimit_AndTheRequestsLeft()
    {
        var owner = await factory.GetOwnerClientAsync();
        var userId = await UserIdAsync(owner);
        await ResetAsync(Enumerable.Range(0, 3).Select(_ => Row(userId, Configured, DateTime.UtcNow, 1, 1)).ToArray());

        var usage = (await owner.GraphQLAsync(Today)).GetProperty("llmUsageToday");

        // 500 is the free-tier limit set in appsettings.json.
        Assert.Equal(500, usage.GetProperty("requestsLimit").GetInt32());
        Assert.Equal(497, usage.GetProperty("remainingRequests").GetInt32());
    }

    [Fact]
    public async Task Today_NeverReportsLessThanZeroLeft()
    {
        var owner = await factory.GetOwnerClientAsync();
        var userId = await UserIdAsync(owner);
        await ResetAsync(Enumerable.Range(0, 501).Select(_ => Row(userId, Configured, DateTime.UtcNow, 1, 1)).ToArray());

        var usage = (await owner.GraphQLAsync(Today)).GetProperty("llmUsageToday");

        Assert.Equal(0, usage.GetProperty("remainingRequests").GetInt32());
    }

    [Fact]
    public async Task Today_WithNoLimitConfigured_HasNoLimitOrRemaining()
    {
        using var unlimited = factory.WithWebHostBuilder(b => b.UseSetting("Llm:Gemini:Models:0:RequestsPerDay", ""));
        var client = unlimited.CreateClient();
        (await client.PostAsJsonAsync("/api/auth/login",
            new JobHunt.Api.Features.Auth.LoginRequest(ApiFactory.OwnerEmail, ApiFactory.OwnerPassword))).EnsureSuccessStatusCode();

        var usage = (await client.GraphQLAsync(Today)).GetProperty("llmUsageToday");

        Assert.Equal(JsonValueKind.Null, usage.GetProperty("requestsLimit").ValueKind);
        Assert.Equal(JsonValueKind.Null, usage.GetProperty("remainingRequests").ValueKind);
    }

    [Fact]
    public async Task Today_FollowsEachUsersDefaultModel_AndItsLimit()
    {
        var owner = await factory.GetOwnerClientAsync();
        var guest = await factory.GetGuestClientAsync();
        var ownerId = await UserIdAsync(owner);
        await ResetAsync(
            Row(ownerId, ApiFactory.StrongModel, DateTime.UtcNow, 1, 1),
            Row(ownerId, ApiFactory.StrongModel, DateTime.UtcNow, 1, 1),
            Row(ownerId, Configured, DateTime.UtcNow, 1, 1));
        await owner.GraphQLAsync("mutation ($m: String) { setDefaultLlmModel(input: { model: $m }) { errors { __typename } } }",
            new { m = ApiFactory.StrongModel });
        try
        {
            var ownerUsage = (await owner.GraphQLAsync(Today)).GetProperty("llmUsageToday");
            var guestUsage = (await guest.GraphQLAsync(Today)).GetProperty("llmUsageToday");

            Assert.Equal(ApiFactory.StrongModel, ownerUsage.GetProperty("model").GetString());
            Assert.Equal(2, ownerUsage.GetProperty("requests").GetInt32());
            // 250 is the test model's limit (ApiFactory); the default keeps its 500.
            Assert.Equal(250, ownerUsage.GetProperty("requestsLimit").GetInt32());
            Assert.Equal(Configured, guestUsage.GetProperty("model").GetString());
            Assert.Equal(1, guestUsage.GetProperty("requests").GetInt32());
            Assert.Equal(500, guestUsage.GetProperty("requestsLimit").GetInt32());
        }
        finally
        {
            await owner.GraphQLAsync("mutation { setDefaultLlmModel(input: { model: null }) { errors { __typename } } }");
        }
    }

    [Fact]
    public void PacificDay_StartsAtLocalMidnight_InWinterAndSummer()
    {
        Assert.Equal(new DateTime(2026, 1, 15, 8, 0, 0), PacificDay.StartUtc(new DateOnly(2026, 1, 15)));
        Assert.Equal(new DateTime(2026, 7, 15, 7, 0, 0), PacificDay.StartUtc(new DateOnly(2026, 7, 15)));
        Assert.Equal(new DateOnly(2026, 1, 15), PacificDay.Of(new DateTime(2026, 1, 16, 7, 59, 0)));
        Assert.Equal(new DateOnly(2026, 1, 16), PacificDay.Of(new DateTime(2026, 1, 16, 8, 0, 0)));
        Assert.Equal(new DateOnly(2026, 7, 15), PacificDay.Of(new DateTime(2026, 7, 16, 6, 59, 0)));
        Assert.Equal(new DateOnly(2026, 7, 16), PacificDay.Of(new DateTime(2026, 7, 16, 7, 0, 0)));
    }

    [Fact]
    public void PacificDay_IsOnly23HoursLong_WhenDaylightSavingStarts()
    {
        var start = PacificDay.StartUtc(new DateOnly(2026, 3, 8));
        var next = PacificDay.StartUtc(new DateOnly(2026, 3, 9));

        Assert.Equal(TimeSpan.FromHours(23), next - start);
    }

    [Fact]
    public async Task Daily_GivesOneRowPerDayAndModel_AcrossAllUsers()
    {
        var owner = await factory.GetOwnerClientAsync();
        var guest = await factory.GetGuestClientAsync();
        var ownerId = await UserIdAsync(owner);
        var guestId = await UserIdAsync(guest);
        await ResetAsync(
            Row(ownerId, Configured, DateTime.UtcNow, 100, 50),
            Row(guestId, Configured, DateTime.UtcNow, 10, 5),
            Row(ownerId, "other-model", DateTime.UtcNow, 1, 1),
            Row(ownerId, Configured, TodayStart.AddMinutes(-30), 9, 9));

        var rows = (await guest.GraphQLAsync(Daily, new { days = 7 })).GetProperty("usageDaily");

        var today = PacificDay.Of(DateTime.UtcNow);
        var summary = rows.EnumerateArray().Select(r =>
            $"{r.GetProperty("day").GetString()} {r.GetProperty("model").GetString()} {r.GetProperty("requests").GetInt32()} " +
            $"{r.GetProperty("inputTokens").GetInt64()}/{r.GetProperty("outputTokens").GetInt64()}");
        Assert.Equal(
        [
            $"{today.AddDays(-1):yyyy-MM-dd} {Configured} 1 9/9",
            $"{today:yyyy-MM-dd} {Configured} 2 110/55",
            $"{today:yyyy-MM-dd} other-model 1 1/1",
        ], summary);
    }

    [Fact]
    public async Task Daily_SplitsDaysAtMidnightPacific()
    {
        var owner = await factory.GetOwnerClientAsync();
        var userId = await UserIdAsync(owner);
        await ResetAsync(
            Row(userId, Configured, TodayStart.AddMinutes(-30), 1, 1),
            Row(userId, Configured, TodayStart.AddMinutes(30), 1, 1));

        var rows = (await owner.GraphQLAsync(Daily, new { days = 3 })).GetProperty("usageDaily");

        Assert.Equal(2, rows.GetArrayLength());
        Assert.NotEqual(rows[0].GetProperty("day").GetString(), rows[1].GetProperty("day").GetString());
    }

    [Fact]
    public async Task Daily_EstimatesCostFromPrices_AndLeavesItNullWithoutThem()
    {
        var owner = await factory.GetOwnerClientAsync();
        var userId = await UserIdAsync(owner);
        await ResetAsync(
            Row(userId, "priced-model", DateTime.UtcNow, 1_000_000, 500_000),
            Row(userId, Configured, DateTime.UtcNow, 1_000_000, 500_000));

        var rows = (await owner.GraphQLAsync(Daily, new { days = 1 })).GetProperty("usageDaily");

        var cost = rows.EnumerateArray().ToDictionary(
            r => r.GetProperty("model").GetString()!, r => r.GetProperty("estimatedCost"));
        // 1M input at 0.10 + 0.5M output at 0.40 per million.
        Assert.Equal(0.30m, cost["priced-model"].GetDecimal());
        Assert.Equal(JsonValueKind.Null, cost[Configured].ValueKind);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(91)]
    public async Task Daily_WithDaysOutOfRange_ReturnsAnError(int days)
    {
        var owner = await factory.GetOwnerClientAsync();

        var response = await owner.PostAsJsonAsync("/graphql", new { query = Daily, variables = new { days } });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.True(body.TryGetProperty("errors", out _));
    }

    private static LlmUsage Row(string userId, string model, DateTime at, long? input, long? output, bool success = true) => new()
    {
        UserId = userId, Provider = GeminiOptions.Provider, Model = model, Operation = "test",
        InputTokens = input, OutputTokens = output, Success = success, CreatedAt = at,
    };

    private static async Task<string> UserIdAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<JsonElement>("/api/auth/me")).GetProperty("id").GetString()!;

    // Tests in a class run one at a time, so each starts from an empty table.
    private async Task ResetAsync(params LlmUsage[] rows)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.LlmUsages.IgnoreQueryFilters().ExecuteDeleteAsync();
        db.LlmUsages.AddRange(rows);
        await db.SaveChangesAsync();
    }
}
