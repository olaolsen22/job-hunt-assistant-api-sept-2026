using System.Text.Json;
using JobHunt.Api.Features.Auth;
using JobHunt.Api.Features.Listings;
using JobHunt.Api.Features.TailoredCvDrafts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobHunt.Api.Features.BaseCvs;

// A reusable CV for a kind of role, sent as-is. Per-language numbered versions keep outcomes comparable.
public class BaseCv
{
    public int Id { get; set; }
    public string UserId { get; set; } = null!;
    public string Name { get; set; } = null!;
    // Names.Normalize(Name): unique per user.
    public string NormalizedName { get; set; } = null!;
    public string? Description { get; set; }
    // Null = active. Archived base CVs leave the picker but keep the versions applications used.
    public DateTime? ArchivedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<BaseCvVersion> Versions { get; set; } = [];
}

// Immutable, so a sent document keeps its content. Only skill and category links move (TailoredCvDraftSkills).
public class BaseCvVersion : ITailoredCvSnapshot
{
    public int Id { get; set; }
    public string UserId { get; set; } = null!;
    public int BaseCvId { get; set; }
    public BaseCv BaseCv { get; set; } = null!;
    public Language Language { get; set; }
    // 1, 2, 3… within its base CV and language.
    public int Number { get; set; }
    // Leaf data in the tailored draft's shape; no query looks inside it.
    public string ContentJson { get; set; } = "{}";
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; }

    public TailoredCvContent Content
    {
        get => JsonSerializer.Deserialize<TailoredCvContent>(ContentJson, TailoredCvJson.Options) ?? new();
        set => ContentJson = JsonSerializer.Serialize(value, TailoredCvJson.Options);
    }
}

public class BaseCvConfiguration : IEntityTypeConfiguration<BaseCv>
{
    public void Configure(EntityTypeBuilder<BaseCv> builder)
    {
        builder.HasOne<AppUser>().WithMany().HasForeignKey(b => b.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(b => new { b.UserId, b.NormalizedName }).IsUnique();
        builder.Property(b => b.Name).HasMaxLength(BaseCvMutations.MaxNameLength);
    }
}

public class BaseCvVersionConfiguration : IEntityTypeConfiguration<BaseCvVersion>
{
    public void Configure(EntityTypeBuilder<BaseCvVersion> builder)
    {
        builder.Ignore(v => v.Content);
        builder.Property(v => v.ContentJson).HasColumnType("TEXT");
        builder.HasOne<AppUser>().WithMany().HasForeignKey(v => v.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(v => v.BaseCv).WithMany(b => b.Versions).HasForeignKey(v => v.BaseCvId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(v => v.UserId);
        builder.HasIndex(v => new { v.BaseCvId, v.Language, v.Number }).IsUnique();
    }
}

[ObjectType<BaseCv>]
public static partial class BaseCvNode
{
    static partial void Configure(IObjectTypeDescriptor<BaseCv> descriptor)
    {
        descriptor.Ignore(b => b.UserId);
        descriptor.Ignore(b => b.NormalizedName);
        // Replaced by GetVersions, which takes a language.
        descriptor.Ignore(b => b.Versions);
    }

    // Every query and mutation loads the versions with the base CV, and there is always at least one.
    // Without a language: the newest version in any language.
    public static BaseCvVersion? GetLatestVersion([Parent] BaseCv baseCv, Language? language = null) =>
        InLanguage(baseCv, language).MaxBy(v => v.Id);

    // Newest first.
    public static List<BaseCvVersion> GetVersions([Parent] BaseCv baseCv, Language? language = null) =>
        [.. InLanguage(baseCv, language).OrderByDescending(v => v.Id)];

    // In the order each language was added.
    public static List<Language> GetLanguages([Parent] BaseCv baseCv) =>
        [.. baseCv.Versions.OrderBy(v => v.Id).Select(v => v.Language).Distinct()];

    private static IEnumerable<BaseCvVersion> InLanguage(BaseCv baseCv, Language? language) =>
        baseCv.Versions.Where(v => language is null || v.Language == language);
}

[ObjectType<BaseCvVersion>]
public static partial class BaseCvVersionNode
{
    static partial void Configure(IObjectTypeDescriptor<BaseCvVersion> descriptor)
    {
        descriptor.Ignore(v => v.UserId);
        descriptor.Ignore(v => v.BaseCvId);
        descriptor.Ignore(v => v.ContentJson);
    }
}
