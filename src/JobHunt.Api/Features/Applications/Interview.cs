using JobHunt.Api.Features.Auth;
using JobHunt.Api.Features.Listings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobHunt.Api.Features.Applications;

public class Interview
{
    public int Id { get; set; }
    public string UserId { get; set; } = null!;
    public int ListingId { get; set; }
    public Listing Listing { get; set; } = null!;
    // Free text ("HR screen", "Tech round 2"); rounds are ordered by ScheduledAt.
    public string? Title { get; set; }
    public DateTime ScheduledAt { get; set; }
    // Markdown.
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class InterviewConfiguration : IEntityTypeConfiguration<Interview>
{
    public void Configure(EntityTypeBuilder<Interview> builder)
    {
        builder.HasOne<AppUser>().WithMany().HasForeignKey(i => i.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(i => i.Listing).WithMany(l => l.Interviews).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(i => new { i.ListingId, i.ScheduledAt });
    }
}
