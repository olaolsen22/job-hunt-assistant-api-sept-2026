using JobHunt.Api.Features.Auth;
using JobHunt.Api.Features.JobRoles;
using JobHunt.Api.Features.Listings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobHunt.Api.Features.Profiles;

// One per user, created by LookupSeeder, so the profile query never returns null. The master copy of the user's data.
public class Profile
{
    public int Id { get; set; }
    public string UserId { get; set; } = null!;
    public string? FullName { get; set; }
    public string? Headline { get; set; }
    // Markdown.
    public string? Summary { get; set; }
    // The language the user's own text is written in; translations exist only in the other one.
    public Language ContentLanguage { get; set; } = Language.English;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    // Sensitive: only stored and shown. AI prompt builders pick fields from an explicit allowlist,
    // so these never reach an LLM.
    public DateOnly? DateOfBirth { get; set; }
    public string? Nationality { get; set; }
    public string? WorkAuthorization { get; set; }
    // Postal address for the cover letter's sender block; the town is Location.City. Never sent to an LLM.
    public string? StreetAddress { get; set; }
    public string? PostalCode { get; set; }
    // Leaf data that is never filtered or matched, so a list column, not rows.
    public List<string> Hobbies { get; set; } = [];
    // Personal background for cover letters only; CV prompts never include it.
    public ProfileStory Story { get; set; } = new();
    // Where the user lives; target cities are Location.IsTarget instead.
    public int? LocationId { get; set; }
    public Location? Location { get; set; }
    // JSON column: display-only, never filtered.
    public List<ProfileLink> Links { get; set; } = [];

    // Job preferences. Roles, work modes and employment types are rows, not JSON, so listings can be matched on them.
    public List<JobRole> JobRoles { get; set; } = [];
    public List<ProfileWorkMode> WorkModes { get; set; } = [];
    public List<ProfileEmploymentType> EmploymentTypes { get; set; } = [];
    public int? MinSalary { get; set; }
    // ISO 4217, required when MinSalary is set.
    public string? SalaryCurrency { get; set; }
    public int? NoticePeriodMonths { get; set; }

    public List<ProfileExperience> Experiences { get; set; } = [];
    public List<ProfileEducation> Educations { get; set; } = [];
    public List<ProfileCertification> Certifications { get; set; } = [];
    public List<ProfileProject> Projects { get; set; } = [];
    public List<ProfileSkill> Skills { get; set; } = [];
    public List<ProfileLanguage> Languages { get; set; } = [];
    public List<ProfileReference> References { get; set; } = [];
    public List<ProfileTranslation> Translations { get; set; } = [];

    // Internal, so Hot Chocolate doesn't expose them. Need the entries loaded.
    internal IEnumerable<List<ProfileBullet>> BulletLists() =>
        Experiences.Select(e => e.Bullets).Concat(Educations.Select(e => e.Bullets))
            .Concat(Certifications.Select(c => c.Bullets)).Concat(Projects.Select(p => p.Bullets));

    internal IEnumerable<ProfileBullet> AllBullets() => BulletLists().SelectMany(b => b);
}

// Names chosen so GraphQL shows GITHUB / LINKEDIN.
public enum LinkKind { Github, Linkedin, Portfolio, Other }

public class ProfileLink
{
    public LinkKind Kind { get; set; }
    // Empty unless Kind is Other: the other kinds are named by the kind.
    public string Label { get; set; } = "";
    public string Url { get; set; } = null!;
}

public class ProfileWorkMode
{
    public int ProfileId { get; set; }
    public Profile Profile { get; set; } = null!;
    public WorkMode WorkMode { get; set; }
}

public class ProfileEmploymentType
{
    public int ProfileId { get; set; }
    public Profile Profile { get; set; } = null!;
    public EmploymentType EmploymentType { get; set; }
}

public class ProfileConfiguration : IEntityTypeConfiguration<Profile>
{
    public void Configure(EntityTypeBuilder<Profile> builder)
    {
        builder.HasOne<AppUser>().WithMany().HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(p => p.UserId).IsUnique();
        builder.HasOne(p => p.Location).WithMany().OnDelete(DeleteBehavior.SetNull);
        builder.ComplexProperty(p => p.Story);
        builder.OwnsMany(p => p.Links, b =>
        {
            b.ToJson();
            // Text, so the stored JSON stays readable and survives reordering the enum.
            b.Property(l => l.Kind).HasConversion<string>();
        });
        builder.HasMany(p => p.JobRoles).WithMany().UsingEntity("ProfileJobRoles",
            r => r.HasOne(typeof(JobRole)).WithMany().HasForeignKey("JobRoleId"),
            l => l.HasOne(typeof(Profile)).WithMany().HasForeignKey("ProfileId"));
    }
}

public class ProfileWorkModeConfiguration : IEntityTypeConfiguration<ProfileWorkMode>
{
    public void Configure(EntityTypeBuilder<ProfileWorkMode> builder)
    {
        builder.ToTable("ProfileWorkModes");
        builder.HasKey(w => new { w.ProfileId, w.WorkMode });
        builder.HasOne(w => w.Profile).WithMany(p => p.WorkModes).OnDelete(DeleteBehavior.Cascade);
    }
}

public class ProfileEmploymentTypeConfiguration : IEntityTypeConfiguration<ProfileEmploymentType>
{
    public void Configure(EntityTypeBuilder<ProfileEmploymentType> builder)
    {
        builder.ToTable("ProfileEmploymentTypes");
        builder.HasKey(t => new { t.ProfileId, t.EmploymentType });
        builder.HasOne(t => t.Profile).WithMany(p => p.EmploymentTypes).OnDelete(DeleteBehavior.Cascade);
    }
}
