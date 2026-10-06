using JobHunt.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace JobHunt.Api.Features.Dashboard;

public record DashboardWidgetState(DashboardWidgetKind Widget, bool IsShown);

[QueryType]
public static class DashboardQueries
{
    // Always one entry per widget. Widgets without a saved row (never saved, or added in a later release)
    // come last in default order and shown, so no data migration is needed when the set grows.
    public static async Task<List<DashboardWidgetState>> GetDashboardLayoutAsync(AppDbContext db, CancellationToken ct)
    {
        var saved = await db.DashboardWidgets.OrderBy(w => w.Position).ToListAsync(ct);
        var layout = saved.Select(w => new DashboardWidgetState(w.Widget, w.IsShown)).ToList();
        var missing = Enum.GetValues<DashboardWidgetKind>().Except(saved.Select(w => w.Widget));
        layout.AddRange(missing.Select(w => new DashboardWidgetState(w, true)));
        return layout;
    }
}
