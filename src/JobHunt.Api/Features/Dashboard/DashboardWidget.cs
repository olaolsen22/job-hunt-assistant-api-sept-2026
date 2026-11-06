using JobHunt.Api.Features.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobHunt.Api.Features.Dashboard;

// Declaration order is the default dashboard order.
public enum DashboardWidgetKind
{
    ClosingSoon,
    UpcomingInterviews,
    OffersToAnswer,
    StaleListings,
    ContactFollowUps,
    Pipeline,
    AppliedThisWeek,
    ToAnalyze,
    FavoritedListings,
    FavoritedCompanies,
    LlmUsage,
}

// One row per widget the user has saved a layout for. Hidden widgets keep their Position so showing one again
// puts it back where it was.
public class DashboardWidget
{
    public int Id { get; set; }
    public string UserId { get; set; } = null!;
    public DashboardWidgetKind Widget { get; set; }
    public bool IsShown { get; set; }
    public int Position { get; set; }
}

public class DashboardWidgetConfiguration : IEntityTypeConfiguration<DashboardWidget>
{
    public void Configure(EntityTypeBuilder<DashboardWidget> builder)
    {
        builder.HasOne<AppUser>().WithMany().HasForeignKey(w => w.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(w => new { w.UserId, w.Widget }).IsUnique();
    }
}
