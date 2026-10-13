using System.Text.Json;
using JobHunt.Api.Features.Auth;
using JobHunt.Api.Features.Listings;
using JobHunt.Api.Features.Profiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobHunt.Api.Features.TailoredCvDrafts;

// The saved copy is application-specific. Profile edits never rewrite this snapshot.
public class TailoredCvDraft
{
    public int Id { get; set; }
    public string UserId { get; set; } = null!;
    public int ListingId { get; set; }
    public Listing Listing { get; set; } = null!;
    // Leaf data: the UI edits it as sections, but no query filters or joins into the JSON.
    public string ContentJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public TailoredCvContent Content
    {
        get => JsonSerializer.Deserialize<TailoredCvContent>(ContentJson, TailoredCvJson.Options) ?? new();
        set => ContentJson = JsonSerializer.Serialize(value, TailoredCvJson.Options);
    }
}

// Language decides headings, labels, and date words in the PDF; it defaults to the listing's posting language.
public record TailoredCvContent(
    string? Summary = null,
    List<TailoredCvExperience>? Experiences = null,
    List<TailoredCvEducation>? Educations = null,
    List<TailoredCvCertification>? Certifications = null,
    List<TailoredCvProject>? Projects = null,
    List<TailoredCvSkill>? Skills = null,
    List<TailoredCvLanguage>? Languages = null,
    string? FullName = null,
    string? Headline = null,
    string? Email = null,
    string? Phone = null,
    string? Address = null,
    List<TailoredCvLink>? Links = null,
    Language Language = Language.English,
    DateOnly? DateOfBirth = null,
    bool ReferencesOnRequest = false);

public record TailoredCvBullet(string Text, int? SourceBulletId = null);
public enum TailoredCvExperienceContentStyle { Bullets, Paragraph }
// Location is display text such as "Lagos, Nigeria"; the guidance favours City, Country for foreign roles.
public record TailoredCvExperience(string Employer, string Title, DateOnly? StartMonth, DateOnly? EndMonth,
    List<TailoredCvBullet> Bullets, int? SourceEntryId = null, string? Location = null,
    TailoredCvExperienceContentStyle ContentStyle = TailoredCvExperienceContentStyle.Bullets, string? Paragraph = null);
public record TailoredCvEducation(string School, string? Degree, string? Field, DateOnly? StartMonth,
    DateOnly? EndMonth, List<TailoredCvBullet> Bullets, int? SourceEntryId = null, string? Location = null);
public record TailoredCvCertification(string Name, string? Issuer, DateOnly? IssuedMonth, DateOnly? ExpiresMonth,
    string? Url, List<TailoredCvBullet> Bullets, int? SourceEntryId = null);
public record TailoredCvProject(string Name, string? Url, DateOnly? StartMonth, DateOnly? EndMonth,
    List<TailoredCvBullet> Bullets, int? SourceEntryId = null, string? Location = null);
public record TailoredCvSkill(string Name, int? Level, int? SourceSkillId = null);
public record TailoredCvLanguage(string Name, LanguageProficiency? Written, LanguageProficiency? Spoken);
public record TailoredCvLink(string Label, string Url);

internal static class TailoredCvJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(
            System.Text.Json.JsonNamingPolicy.SnakeCaseUpper, allowIntegerValues: false) },
    };
}

public class TailoredCvDraftConfiguration : IEntityTypeConfiguration<TailoredCvDraft>
{
    public void Configure(EntityTypeBuilder<TailoredCvDraft> builder)
    {
        builder.Ignore(d => d.Content);
        builder.Property(d => d.ContentJson).HasColumnType("TEXT");
        builder.HasOne<AppUser>().WithMany().HasForeignKey(d => d.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(d => d.Listing).WithMany().HasForeignKey(d => d.ListingId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(d => d.UserId);
        builder.HasIndex(d => d.ListingId).IsUnique();
    }
}
