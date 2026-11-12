using System.Text.Json;
using JobHunt.Api.Data;
using JobHunt.Api.Features.Auth;
using JobHunt.Api.Features.Listings;
using JobHunt.Api.Features.Profiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobHunt.Api.Features.TailoredCvDrafts;

// CV content saved as a snapshot. Skill and category changes move their links, never their text.
public interface ITailoredCvSnapshot
{
    TailoredCvContent Content { get; set; }
}

// The saved copy is application-specific. Profile edits never rewrite this snapshot.
public class TailoredCvDraft : ITailoredCvSnapshot
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
    bool ReferencesOnRequest = false,
    TailoredCvSectionOrder SectionOrder = TailoredCvSectionOrder.Standard,
    TailoredCvContentStyle ContentStyle = TailoredCvContentStyle.Bullets,
    TailoredCvTemplate Template = TailoredCvTemplate.Detailed)
{
    // Every section with bullets, with what a bullet's source must belong to.
    internal IEnumerable<(string Kind, int? EntryId, List<TailoredCvBullet> Bullets)> BulletGroups() =>
        (Experiences ?? []).Select(e => ("experience", e.SourceEntryId, e.Bullets))
            .Concat((Educations ?? []).Select(e => ("education", e.SourceEntryId, e.Bullets)))
            .Concat((Certifications ?? []).Select(e => ("certification", e.SourceEntryId, e.Bullets)))
            .Concat((Projects ?? []).Select(e => ("project", e.SourceEntryId, e.Bullets)));

    internal TailoredCvContent MapBullets(Func<List<TailoredCvBullet>, List<TailoredCvBullet>> map) => this with
    {
        Experiences = Experiences?.Select(e => e with { Bullets = map(e.Bullets) }).ToList(),
        Educations = Educations?.Select(e => e with { Bullets = map(e.Bullets) }).ToList(),
        Certifications = Certifications?.Select(e => e with { Bullets = map(e.Bullets) }).ToList(),
        Projects = Projects?.Select(e => e with { Bullets = map(e.Bullets) }).ToList(),
    };

    // Replaces each entry's paragraph with map(its bullets, its paragraph).
    internal TailoredCvContent MapParagraphs(Func<List<TailoredCvBullet>, string?, string?> map) => this with
    {
        Experiences = Experiences?.Select(e => e with { Paragraph = map(e.Bullets, e.Paragraph) }).ToList(),
        Educations = Educations?.Select(e => e with { Paragraph = map(e.Bullets, e.Paragraph) }).ToList(),
        Certifications = Certifications?.Select(e => e with { Paragraph = map(e.Bullets, e.Paragraph) }).ToList(),
        Projects = Projects?.Select(e => e with { Paragraph = map(e.Bullets, e.Paragraph) }).ToList(),
    };
}

// Standard: NAV's order. Developer: links with contact lines, skills and projects earlier
// (docs/norwegian-cv-formatting.md §10).
public enum TailoredCvSectionOrder { Standard, Developer }

public record TailoredCvBullet(string Text, int? SourceBulletId = null);
// One style for the whole CV, so entries print consistently: as bullets or as one paragraph each.
public enum TailoredCvContentStyle { Bullets, Paragraph }
// Detailed keeps full bullets (worded to the listing in a tailored CV); Concise shortens them for a one-page CV a recruiter can scan
// in seconds. TailoredCvWording.ModeFor and CvGuidance.Layout hold what each asks of the AI.
public enum TailoredCvTemplate { Detailed, Concise }
// Location is display text ("Lagos, Nigeria"). Technologies print under the entry in the developer order.
public record TailoredCvExperience(string Employer, string Title, DateOnly? StartMonth, DateOnly? EndMonth,
    List<TailoredCvBullet> Bullets, int? SourceEntryId = null, string? Location = null,
    string? Paragraph = null, List<string>? Technologies = null);
public record TailoredCvEducation(string School, string? Degree, string? Field, DateOnly? StartMonth,
    DateOnly? EndMonth, List<TailoredCvBullet> Bullets, int? SourceEntryId = null, string? Location = null,
    string? Paragraph = null);
public record TailoredCvCertification(string Name, string? Issuer, DateOnly? IssuedMonth, DateOnly? ExpiresMonth,
    string? Url, List<TailoredCvBullet> Bullets, int? SourceEntryId = null,
    string? Paragraph = null);
public record TailoredCvProject(string Name, string? Url, DateOnly? StartMonth, DateOnly? EndMonth,
    List<TailoredCvBullet> Bullets, int? SourceEntryId = null, string? Location = null,
    string? Paragraph = null, ProjectKind? Kind = null, List<string>? Technologies = null);
// CategoryId overrides per CV; the PDF reads the category's current name. Group is a CV-only label that beats it.
// List order is relevance order and prints as is.
public record TailoredCvSkill(string Name, int? Level, int? SourceSkillId = null, int? CategoryId = null,
    string? Group = null);
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

// Snapshots only move their links on a skill or category change: a dangling id would block the next save.
public static class TailoredCvDraftSkills
{
    public static void Rewrite(IEnumerable<ITailoredCvSnapshot> snapshots, Func<TailoredCvSkill, TailoredCvSkill> map)
    {
        foreach (var snapshot in snapshots)
        {
            var content = snapshot.Content;
            if (content.Skills is not { } skills) continue;
            var mapped = skills.Select(map).ToList();
            if (!mapped.SequenceEqual(skills)) snapshot.Content = content with { Skills = mapped };
        }
    }

    public static async Task<List<ITailoredCvSnapshot>> LoadAllAsync(AppDbContext db, CancellationToken ct) =>
        [.. await db.TailoredCvDrafts.ToListAsync(ct), .. await db.BaseCvVersions.ToListAsync(ct)];
}
