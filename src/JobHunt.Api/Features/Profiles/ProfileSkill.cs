using JobHunt.Api.Features.Listings;
using JobHunt.Api.Features.Skills;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobHunt.Api.Features.Profiles;

// Same Skill lookup and 1–5 scale as ListingSkill, so matching a listing is a join on SkillId.
public class ProfileSkill
{
    public int ProfileId { get; set; }
    public Profile Profile { get; set; } = null!;
    public int SkillId { get; set; }
    public Skill Skill { get; set; } = null!;
    public int Level { get; set; }
}

// Same enums as ListingLanguage, for the same reason.
public class ProfileLanguage
{
    public int ProfileId { get; set; }
    public Profile Profile { get; set; } = null!;
    public Language Language { get; set; }
    public CefrLevel Level { get; set; }
}

public class ProfileSkillConfiguration : IEntityTypeConfiguration<ProfileSkill>
{
    public void Configure(EntityTypeBuilder<ProfileSkill> builder)
    {
        builder.HasKey(s => new { s.ProfileId, s.SkillId });
        builder.HasOne(s => s.Profile).WithMany(p => p.Skills).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(s => s.Skill).WithMany().OnDelete(DeleteBehavior.Cascade);
    }
}

public class ProfileLanguageConfiguration : IEntityTypeConfiguration<ProfileLanguage>
{
    public void Configure(EntityTypeBuilder<ProfileLanguage> builder)
    {
        builder.HasKey(l => new { l.ProfileId, l.Language });
        builder.HasOne(l => l.Profile).WithMany(p => p.Languages).OnDelete(DeleteBehavior.Cascade);
    }
}
