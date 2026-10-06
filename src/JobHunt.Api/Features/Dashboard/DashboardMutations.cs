using JobHunt.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace JobHunt.Api.Features.Dashboard;

public class InvalidDashboardLayoutException()
    : Exception("Send every dashboard widget exactly once.");

[MutationType]
public static class DashboardMutations
{
    // The list order is the display order. Full replace, so the UI can send the whole list after any drag or toggle.
    [Error<InvalidDashboardLayoutException>]
    [UseMutationConvention(PayloadFieldName = "layout")]
    public static async Task<List<DashboardWidgetState>> SaveDashboardLayoutAsync(
        List<DashboardWidgetState> widgets, AppDbContext db, CancellationToken ct)
    {
        if (!widgets.Select(w => w.Widget).Order().SequenceEqual(Enum.GetValues<DashboardWidgetKind>()))
            throw new InvalidDashboardLayoutException();

        await db.DashboardWidgets.ExecuteDeleteAsync(ct);
        db.DashboardWidgets.AddRange(widgets.Select((w, i) => new DashboardWidget
        {
            UserId = db.CurrentUserId!, Widget = w.Widget, IsShown = w.IsShown, Position = i,
        }));
        await db.SaveChangesAsync(ct);
        return widgets;
    }
}
