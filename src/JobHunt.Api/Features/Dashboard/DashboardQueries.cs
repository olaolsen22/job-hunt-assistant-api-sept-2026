using JobHunt.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace JobHunt.Api.Features.Dashboard;

public record DashboardWidgetState(DashboardWidgetKind Widget, bool IsShown);

[QueryType]
public static class DashboardQueries
{
    // One entry per widget. Widgets without a saved row come last and shown, so new ones need no data migration.
    public static async Task<List<DashboardWidgetState>> GetDashboardLayoutAsync(AppDbContext db, CancellationToken ct)
    {
        var saved = await db.DashboardWidgets.OrderBy(w => w.Position).ToListAsync(ct);
        var layout = saved.Select(w => new DashboardWidgetState(w.Widget, w.IsShown)).ToList();
        var missing = Enum.GetValues<DashboardWidgetKind>().Except(saved.Select(w => w.Widget));
        layout.AddRange(missing.Select(w => new DashboardWidgetState(w, true)));
        return layout;
    }
}
