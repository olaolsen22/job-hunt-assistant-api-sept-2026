using System.Net.Http.Json;
using System.Text.Json;
using JobHunt.Api.Data;
using JobHunt.Api.Features.Dashboard;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace JobHunt.Api.Tests.Features.Dashboard;

public class DashboardTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly string[] Default =
    [
        "CLOSING_SOON", "UPCOMING_INTERVIEWS", "OFFERS_TO_ANSWER", "STALE_LISTINGS", "CONTACT_FOLLOW_UPS",
        "PIPELINE",
        "APPLIED_THIS_WEEK", "TO_ANALYZE", "FAVORITED_LISTINGS", "FAVORITED_COMPANIES",
        "LLM_USAGE",
    ];

    private const string Query = "{ dashboardLayout { widget isShown } }";

    private const string Save = """
        mutation ($widgets: [DashboardWidgetStateInput!]!) {
          saveDashboardLayout(input: { widgets: $widgets }) {
            layout { widget isShown }
            errors { __typename }
          }
        }
        """;

    [Fact]
    public async Task NeverSaved_ReturnsAllWidgetsShownInDefaultOrder()
    {
        var client = await factory.GetGuestClientAsync();

        var layout = (await client.GraphQLAsync(Query)).GetProperty("dashboardLayout");

        Assert.Equal(Default, Widgets(layout));
        Assert.All(layout.EnumerateArray(), w => Assert.True(w.GetProperty("isShown").GetBoolean()));
    }

    [Fact]
    public async Task Save_StoresOrderAndVisibility_AndReplacesThePreviousLayout()
    {
        var client = await factory.GetOwnerClientAsync();
        var reversed = Default.Reverse().ToArray();

        await client.GraphQLAsync(Save, new { widgets = Input(Default, hidden: "PIPELINE") });
        await client.GraphQLAsync(Save, new { widgets = Input(reversed, hidden: "TO_ANALYZE") });
        var layout = (await client.GraphQLAsync(Query)).GetProperty("dashboardLayout");

        Assert.Equal(reversed, Widgets(layout));
        var shown = layout.EnumerateArray().ToDictionary(
            w => w.GetProperty("widget").GetString()!, w => w.GetProperty("isShown").GetBoolean());
        Assert.False(shown["TO_ANALYZE"]);
        Assert.True(shown["PIPELINE"]);
    }

    [Fact]
    public async Task HiddenWidget_KeepsItsPlaceWhenShownAgain()
    {
        var client = await factory.GetOwnerClientAsync();

        await client.GraphQLAsync(Save, new { widgets = Input(Default, hidden: Default[2]) });
        var hidden = (await client.GraphQLAsync(Query)).GetProperty("dashboardLayout");
        await client.GraphQLAsync(Save, new { widgets = Input(Default) });
        var shown = (await client.GraphQLAsync(Query)).GetProperty("dashboardLayout");

        Assert.Equal(Default[2], hidden[2].GetProperty("widget").GetString());
        Assert.False(hidden[2].GetProperty("isShown").GetBoolean());
        Assert.Equal(Default[2], shown[2].GetProperty("widget").GetString());
        Assert.True(shown[2].GetProperty("isShown").GetBoolean());
    }

    [Fact]
    public async Task Save_WithMissingOrDuplicateWidget_FailsAndKeepsTheStoredLayout()
    {
        var client = await factory.GetOwnerClientAsync();
        var reversed = Default.Reverse().ToArray();
        await client.GraphQLAsync(Save, new { widgets = Input(reversed) });

        var missing = await client.GraphQLAsync(Save, new { widgets = Input(Default.Skip(1).ToArray()) });
        var duplicate = await client.GraphQLAsync(Save, new { widgets = Input([.. Default.Skip(1), Default[1]]) });
        var layout = (await client.GraphQLAsync(Query)).GetProperty("dashboardLayout");

        foreach (var result in new[] { missing, duplicate })
            Assert.Equal("InvalidDashboardLayoutError",
                result.GetProperty("saveDashboardLayout").GetProperty("errors")[0].GetProperty("__typename").GetString());
        Assert.Equal(reversed, Widgets(layout));
    }

    [Fact]
    public async Task Layouts_ArePerUser()
    {
        var owner = await factory.GetOwnerClientAsync();
        var guest = await factory.GetGuestClientAsync();

        await owner.GraphQLAsync(Save, new { widgets = Input(Default.Reverse().ToArray()) });
        var layout = (await guest.GraphQLAsync(Query)).GetProperty("dashboardLayout");

        Assert.Equal(Default, Widgets(layout));
    }

    [Fact]
    public async Task LayoutSavedBeforeNewWidgetsExisted_GetsThemAppendedInOrderAndShown()
    {
        var client = await factory.GetOwnerClientAsync();
        var userId = (await client.GetFromJsonAsync<JsonElement>("/api/auth/me")).GetProperty("id").GetString()!;
        string[] added = ["CONTACT_FOLLOW_UPS", "LLM_USAGE"];
        var oldNine = Default.Except(added).Reverse().ToArray();

        // The API can't save nine widgets any more, so write the rows an older release would have left behind.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.DashboardWidgets.IgnoreQueryFilters().Where(w => w.UserId == userId).ExecuteDeleteAsync();
            db.DashboardWidgets.AddRange(oldNine.Select((w, i) => new DashboardWidget
            {
                UserId = userId, Widget = Enum.Parse<DashboardWidgetKind>(w.Replace("_", ""), ignoreCase: true),
                IsShown = true, Position = i,
            }));
            await db.SaveChangesAsync();
        }
        var layout = (await client.GraphQLAsync(Query)).GetProperty("dashboardLayout");

        string[] expected = [.. oldNine, .. added];
        Assert.Equal(expected, Widgets(layout));
        Assert.True(layout[9].GetProperty("isShown").GetBoolean());
        Assert.True(layout[10].GetProperty("isShown").GetBoolean());
    }

    [Fact]
    public async Task Save_WithoutLlmUsage_FailsAndKeepsTheStoredLayout()
    {
        var client = await factory.GetOwnerClientAsync();
        var reversed = Default.Reverse().ToArray();
        await client.GraphQLAsync(Save, new { widgets = Input(reversed) });

        var result = await client.GraphQLAsync(Save, new { widgets = Input(Default[..^1]) });
        var layout = (await client.GraphQLAsync(Query)).GetProperty("dashboardLayout");

        Assert.Equal("InvalidDashboardLayoutError",
            result.GetProperty("saveDashboardLayout").GetProperty("errors")[0].GetProperty("__typename").GetString());
        Assert.Equal(reversed, Widgets(layout));
    }

    private static string?[] Widgets(JsonElement layout) =>
        [.. layout.EnumerateArray().Select(w => w.GetProperty("widget").GetString())];

    private static object[] Input(string[] widgets, string? hidden = null) =>
        [.. widgets.Select(w => new { widget = w, isShown = w != hidden })];
}
