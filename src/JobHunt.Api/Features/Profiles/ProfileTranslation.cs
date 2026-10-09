using JobHunt.Api.Features.Auth;
using JobHunt.Api.Features.Listings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobHunt.Api.Features.Profiles;

// The bullet stays the original; a translation hangs off its id, so it survives edits and moves.
public class ProfileBulletTranslation
{
    public int Id { get; set; }
    public string UserId { get; set; } = null!;
    public int BulletId { get; set; }
    public ProfileBullet Bullet { get; set; } = null!;
    public Language Language { get; set; }
    public string Text { get; set; } = null!;
    // The bullet text this was made from. Compared with the bullet instead of a stored flag, so editing the
    // bullet has nothing to update and the UI can show what changed.
    public string SourceText { get; set; } = null!;
    // Set by a future auto-translate; saving by hand clears it.
    public bool IsMachine { get; set; }

    public bool IsStale => Bullet.Text != SourceText;
}

// Headline and summary of the profile in the other language. Each field is checked against its own source copy.
public class ProfileTranslation
{
    public int Id { get; set; }
    public string UserId { get; set; } = null!;
    public int ProfileId { get; set; }
    public Profile Profile { get; set; } = null!;
    public Language Language { get; set; }
    public string? Headline { get; set; }
    public string? Summary { get; set; }
    public string? SourceHeadline { get; set; }
    public string? SourceSummary { get; set; }
    public bool IsMachine { get; set; }

    // An absent translated field is never stale.
    public bool IsHeadlineStale => Headline is not null && Profile.Headline != SourceHeadline;
    public bool IsSummaryStale => Summary is not null && Profile.Summary != SourceSummary;
}

public class ProfileBulletTranslationConfiguration : IEntityTypeConfiguration<ProfileBulletTranslation>
{
    public void Configure(EntityTypeBuilder<ProfileBulletTranslation> builder)
    {
        builder.HasOne<AppUser>().WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(t => t.Bullet).WithMany(b => b.Translations).HasForeignKey(t => t.BulletId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(t => new { t.BulletId, t.Language }).IsUnique();
    }
}

public class ProfileTranslationConfiguration : IEntityTypeConfiguration<ProfileTranslation>
{
    public void Configure(EntityTypeBuilder<ProfileTranslation> builder)
    {
        builder.HasOne<AppUser>().WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(t => t.Profile).WithMany(p => p.Translations).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(t => new { t.ProfileId, t.Language }).IsUnique();
    }
}
