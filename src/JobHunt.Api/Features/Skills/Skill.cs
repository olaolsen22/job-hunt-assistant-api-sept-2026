using JobHunt.Api.Features.Auth;
using JobHunt.Api.Features.Listings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobHunt.Api.Features.Skills;

// Shared by listings and the profile, so matching a listing is a join on SkillId.
public class Skill
{
    public int Id { get; set; }
    public string UserId { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string NormalizedName { get; set; } = null!;
    // Null = uncategorized. Only developer-order CVs group by it, so non-technical skills can stay null.
    public int? CategoryId { get; set; }
    public SkillCategory? Category { get; set; }
    // Only for skills that are words ("Code review" → "Kodegjennomgang"); product names have none and print as Name.
    public List<SkillTranslation> Translations { get; set; } = [];

    public string NameIn(Language language) => Translations.FirstOrDefault(t => t.Language == language)?.Name ?? Name;
}

public class SkillTranslation
{
    public const int MaxNameLength = 200;

    public int Id { get; set; }
    public int SkillId { get; set; }
    public Skill Skill { get; set; } = null!;
    public Language Language { get; set; }
    public string Name { get; set; } = null!;
}

public class SkillConfiguration : IEntityTypeConfiguration<Skill>
{
    public void Configure(EntityTypeBuilder<Skill> builder)
    {
        builder.HasOne<AppUser>().WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(s => new { s.UserId, s.NormalizedName }).IsUnique();
        builder.HasOne(s => s.Category).WithMany().HasForeignKey(s => s.CategoryId).OnDelete(DeleteBehavior.SetNull);
    }
}

public class SkillTranslationConfiguration : IEntityTypeConfiguration<SkillTranslation>
{
    public void Configure(EntityTypeBuilder<SkillTranslation> builder)
    {
        builder.Property(t => t.Name).HasMaxLength(SkillTranslation.MaxNameLength);
        builder.HasIndex(t => new { t.SkillId, t.Language }).IsUnique();
        builder.HasOne(t => t.Skill).WithMany(s => s.Translations).OnDelete(DeleteBehavior.Cascade);
    }
}
