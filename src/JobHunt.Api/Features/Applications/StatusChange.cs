using JobHunt.Api.Features.Auth;
using JobHunt.Api.Features.Listings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobHunt.Api.Features.Applications;

// One row per step of an application: the timeline, and the source for dashboard stats like response time.
public class StatusChange
{
    public int Id { get; set; }
    public string UserId { get; set; } = null!;
    public int ListingId { get; set; }
    public Listing Listing { get; set; } = null!;
    public ApplicationStatus Status { get; set; }
    // When it happened, set by the user because steps are often logged afterwards. CreatedAt is when it was logged.
    public DateTime OccurredAt { get; set; }
    // Markdown.
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; }
}

// Ghosted is not a status: it's derived (no change and no interview for N days) so it can't go stale.
public enum ApplicationStatus { Saved, Applied, Interviewing, Offer, Accepted, Rejected, Withdrawn }

public enum ApplicationOutcome { Offer, Accepted, Rejected, Withdrawn }

public static class ApplicationStatuses
{
    // Closed: the same URL can be logged again. The partial unique index on Listing.Url repeats this list in SQL.
    public static readonly ApplicationStatus[] Final =
        [ApplicationStatus.Rejected, ApplicationStatus.Withdrawn, ApplicationStatus.Accepted];
}

public class StatusChangeConfiguration : IEntityTypeConfiguration<StatusChange>
{
    public void Configure(EntityTypeBuilder<StatusChange> builder)
    {
        builder.HasOne<AppUser>().WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(s => s.Listing).WithMany(l => l.StatusHistory).OnDelete(DeleteBehavior.Cascade);
    }
}
