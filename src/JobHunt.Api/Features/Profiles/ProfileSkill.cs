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

// NAV's CV scale. Not CEFR like ListingLanguage: CVs describe ability in words, written and spoken separately.
public enum LanguageProficiency { Beginner, Good, VeryGood, Native }

// Any language by name (e.g. "Filipino/Tagalog"), unlike the two-value Language enum used for content language.
public class ProfileLanguage
{
    public const int MaxNameLength = 50;

    public int Id { get; set; }
    public int ProfileId { get; set; }
    public Profile Profile { get; set; } = null!;
    public string Name { get; set; } = null!;
    public LanguageProficiency Written { get; set; }
    public LanguageProficiency Spoken { get; set; }
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
        // NOCASE folds ASCII only; enough for language names like "English"/"english".
        builder.Property(l => l.Name).HasMaxLength(ProfileLanguage.MaxNameLength).UseCollation("NOCASE");
        builder.HasIndex(l => new { l.ProfileId, l.Name }).IsUnique();
        builder.HasOne(l => l.Profile).WithMany(p => p.Languages).OnDelete(DeleteBehavior.Cascade);
    }
}
