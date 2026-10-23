using JobHunt.Api.Features.Auth;
using JobHunt.Api.Features.Skills;
// Hot Chocolate also has a global Location type (a position in a query document).
using Location = JobHunt.Api.Features.Locations.Location;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobHunt.Api.Features.Profiles;

// Dates on entries are month precision: always the 1st of the month (ProfileMutations.Month). No end = current.

public class ProfileExperience
{
    public int Id { get; set; }
    public string UserId { get; set; } = null!;
    public int ProfileId { get; set; }
    public Profile Profile { get; set; } = null!;
    // Free text, not a Company: past employers would otherwise show up in the target companies list.
    public string Employer { get; set; } = null!;
    public string Title { get; set; } = null!;
    public int? LocationId { get; set; }
    public Location? Location { get; set; }
    public DateOnly StartMonth { get; set; }
    public DateOnly? EndMonth { get; set; }
    public List<ProfileBullet> Bullets { get; set; } = [];
    // Tagged skills; months of experience per skill are computed from these.
    public List<Skill> Skills { get; set; } = [];
}

public class ProfileEducation
{
    public int Id { get; set; }
    public string UserId { get; set; } = null!;
    public int ProfileId { get; set; }
    public Profile Profile { get; set; } = null!;
    public string School { get; set; } = null!;
    public string? Degree { get; set; }
    public string? Field { get; set; }
    public int? LocationId { get; set; }
    public Location? Location { get; set; }
    public DateOnly StartMonth { get; set; }
    public DateOnly? EndMonth { get; set; }
    public List<ProfileBullet> Bullets { get; set; } = [];
}

public class ProfileCertification
{
    public int Id { get; set; }
    public string UserId { get; set; } = null!;
    public int ProfileId { get; set; }
    public Profile Profile { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? Issuer { get; set; }
    public DateOnly? IssuedMonth { get; set; }
    public DateOnly? ExpiresMonth { get; set; }
    public string? Url { get; set; }
    public List<ProfileBullet> Bullets { get; set; } = [];
}

// Readers must be able to tell professional work from coursework and personal projects (Norwegian CV guidance).
public enum ProjectKind { Work, Personal, Coursework, OpenSource }

public class ProfileProject
{
    public int Id { get; set; }
    public string UserId { get; set; } = null!;
    public int ProfileId { get; set; }
    public Profile Profile { get; set; } = null!;
    public string Name { get; set; } = null!;
    // Null = not stated (older and imported projects).
    public ProjectKind? Kind { get; set; }
    public string? Url { get; set; }
    public int? LocationId { get; set; }
    public Location? Location { get; set; }
    public DateOnly? StartMonth { get; set; }
    public DateOnly? EndMonth { get; set; }
    public List<ProfileBullet> Bullets { get; set; } = [];
    // Shown on the profile, but not counted as months of experience: side projects aren't work years.
    public List<Skill> Skills { get; set; } = [];
}

// The only free text on an entry. A row (not JSON) so a bullet keeps its id when edited or moved, and something
// outside the profile can refer to it later. Belongs to exactly one entry; one table keeps a single id space.
public class ProfileBullet
{
    public int Id { get; set; }
    public string UserId { get; set; } = null!;
    public string Text { get; set; } = null!;
    // 0-based and consecutive within the entry; the mutations renumber on move and delete.
    public int Position { get; set; }
    public int? ExperienceId { get; set; }
    public int? EducationId { get; set; }
    public int? CertificationId { get; set; }
    public int? ProjectId { get; set; }
    public List<ProfileBulletTranslation> Translations { get; set; } = [];
}

public class ProfileBulletConfiguration : IEntityTypeConfiguration<ProfileBullet>
{
    public void Configure(EntityTypeBuilder<ProfileBullet> builder)
    {
        builder.HasOne<AppUser>().WithMany().HasForeignKey(b => b.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ProfileExperience>().WithMany(e => e.Bullets).HasForeignKey(b => b.ExperienceId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ProfileEducation>().WithMany(e => e.Bullets).HasForeignKey(b => b.EducationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ProfileCertification>().WithMany(c => c.Bullets).HasForeignKey(b => b.CertificationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ProfileProject>().WithMany(p => p.Bullets).HasForeignKey(b => b.ProjectId).OnDelete(DeleteBehavior.Cascade);
        // Partial: three of the four are null on every row.
        builder.HasIndex(b => new { b.ExperienceId, b.Position }).HasFilter("\"ExperienceId\" IS NOT NULL");
        builder.HasIndex(b => new { b.EducationId, b.Position }).HasFilter("\"EducationId\" IS NOT NULL");
        builder.HasIndex(b => new { b.CertificationId, b.Position }).HasFilter("\"CertificationId\" IS NOT NULL");
        builder.HasIndex(b => new { b.ProjectId, b.Position }).HasFilter("\"ProjectId\" IS NOT NULL");
        // A bullet with no entry, or two, can't exist, whatever the mutations do.
        builder.ToTable(t => t.HasCheckConstraint("CK_ProfileBullets_OneEntry",
            "(\"ExperienceId\" IS NOT NULL) + (\"EducationId\" IS NOT NULL) + (\"CertificationId\" IS NOT NULL) + (\"ProjectId\" IS NOT NULL) = 1"));
    }
}

public class ProfileExperienceConfiguration : IEntityTypeConfiguration<ProfileExperience>
{
    public void Configure(EntityTypeBuilder<ProfileExperience> builder)
    {
        builder.HasOne<AppUser>().WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(e => e.Profile).WithMany(p => p.Experiences).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(e => e.Location).WithMany().OnDelete(DeleteBehavior.SetNull);
        builder.HasMany(e => e.Skills).WithMany().UsingEntity("ProfileExperienceSkills",
            r => r.HasOne(typeof(Skill)).WithMany().HasForeignKey("SkillId"),
            l => l.HasOne(typeof(ProfileExperience)).WithMany().HasForeignKey("ExperienceId"));
    }
}

public class ProfileEducationConfiguration : IEntityTypeConfiguration<ProfileEducation>
{
    public void Configure(EntityTypeBuilder<ProfileEducation> builder)
    {
        builder.HasOne<AppUser>().WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(e => e.Profile).WithMany(p => p.Educations).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(e => e.Location).WithMany().OnDelete(DeleteBehavior.SetNull);
    }
}

public class ProfileCertificationConfiguration : IEntityTypeConfiguration<ProfileCertification>
{
    public void Configure(EntityTypeBuilder<ProfileCertification> builder)
    {
        builder.HasOne<AppUser>().WithMany().HasForeignKey(c => c.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(c => c.Profile).WithMany(p => p.Certifications).OnDelete(DeleteBehavior.Cascade);
    }
}

public class ProfileProjectConfiguration : IEntityTypeConfiguration<ProfileProject>
{
    public void Configure(EntityTypeBuilder<ProfileProject> builder)
    {
        builder.HasOne<AppUser>().WithMany().HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(p => p.Profile).WithMany(p => p.Projects).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(p => p.Location).WithMany().OnDelete(DeleteBehavior.SetNull);
        builder.HasMany(p => p.Skills).WithMany().UsingEntity("ProfileProjectSkills",
            r => r.HasOne(typeof(Skill)).WithMany().HasForeignKey("SkillId"),
            l => l.HasOne(typeof(ProfileProject)).WithMany().HasForeignKey("ProjectId"));
    }
}
