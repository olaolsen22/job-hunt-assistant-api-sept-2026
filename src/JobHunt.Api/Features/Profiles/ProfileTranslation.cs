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
    // The bullet text this was made from, compared with the bullet so edits need no stale flag.
    public string SourceText { get; set; } = null!;
    // Set when applyProfileTranslation wrote it; saving by hand clears it.
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

// The job title in the other language. Same shape as ProfileTranslation, per experience.
public class ProfileExperienceTranslation
{
    public int Id { get; set; }
    public string UserId { get; set; } = null!;
    public int ExperienceId { get; set; }
    public ProfileExperience Experience { get; set; } = null!;
    public Language Language { get; set; }
    public string Title { get; set; } = null!;
    public string SourceTitle { get; set; } = null!;
    public bool IsMachine { get; set; }

    public bool IsStale => Experience.Title != SourceTitle;
}

// Degree and field in the other language; the school name is a proper name and stays as it is.
public class ProfileEducationTranslation
{
    public int Id { get; set; }
    public string UserId { get; set; } = null!;
    public int EducationId { get; set; }
    public ProfileEducation Education { get; set; } = null!;
    public Language Language { get; set; }
    public string? Degree { get; set; }
    public string? Field { get; set; }
    public string? SourceDegree { get; set; }
    public string? SourceField { get; set; }
    public bool IsMachine { get; set; }

    public bool IsDegreeStale => Degree is not null && Education.Degree != SourceDegree;
    public bool IsFieldStale => Field is not null && Education.Field != SourceField;
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

public class ProfileExperienceTranslationConfiguration : IEntityTypeConfiguration<ProfileExperienceTranslation>
{
    public void Configure(EntityTypeBuilder<ProfileExperienceTranslation> builder)
    {
        builder.HasOne<AppUser>().WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(t => t.Experience).WithMany(e => e.Translations).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(t => new { t.ExperienceId, t.Language }).IsUnique();
    }
}

public class ProfileEducationTranslationConfiguration : IEntityTypeConfiguration<ProfileEducationTranslation>
{
    public void Configure(EntityTypeBuilder<ProfileEducationTranslation> builder)
    {
        builder.HasOne<AppUser>().WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(t => t.Education).WithMany(e => e.Translations).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(t => new { t.EducationId, t.Language }).IsUnique();
    }
}
