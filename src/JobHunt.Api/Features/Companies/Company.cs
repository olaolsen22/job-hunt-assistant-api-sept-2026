using JobHunt.Api.Features.Auth;
using JobHunt.Api.Features.Contacts;
using JobHunt.Api.Features.Industries;
using JobHunt.Api.Features.Listings;
using JobHunt.Api.Features.Skills;
// Hot Chocolate also has a global Location type (a position in a query document).
using Location = JobHunt.Api.Features.Locations.Location;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobHunt.Api.Features.Companies;

public class Company
{
    public int Id { get; set; }
    public string UserId { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string NormalizedName { get; set; } = null!;
    public int? IndustryId { get; set; }
    public Industry? Industry { get; set; }
    // Null = not favorited. A time rather than a bool so the shortlist can sort by when you favorited.
    public DateTime? FavoritedAt { get; set; }
    // Null = not avoided. Independent of FavoritedAt: the why goes in Notes, which any company can have.
    public DateTime? AvoidedAt { get; set; }
    // Markdown; the UI sanitizes on render.
    public string? Notes { get; set; }
    // Offices, in no particular role: a listing's own locations can differ.
    public List<CompanyLocation> Locations { get; set; } = [];
    public List<Contact> Contacts { get; set; } = [];

    // Research: filled by hand or by a pasted research batch, which only fills blanks.
    // Brønnøysund org.nr, 9 digits: a better match key than the name when research comes back in.
    public string? OrgNumber { get; set; }
    public string? WebsiteUrl { get; set; }
    public string? CareersUrl { get; set; }
    public string? LinkedInUrl { get; set; }
    public CompanyKind? Kind { get; set; }
    public CompanySize? SizeBand { get; set; }
    public int? FoundedYear { get; set; }
    public string? Description { get; set; }
    // General remote policy; a listing's own WorkMode can differ.
    public WorkMode? WorkMode { get; set; }
    public Language? WorkingLanguage { get; set; }
    // Last time a research batch created or filled this company; manual edits leave it alone.
    public DateTime? ResearchedAt { get; set; }
    // Tech stack, sharing skill ids with listings and the profile.
    public List<CompanySkill> Skills { get; set; } = [];
}

// Recruitment places candidates with other employers or hires out staff; Broker matches independent consultants to
// client assignments. Both are kept apart from Consultancy, which employs its own consultants.
public enum CompanyKind { Product, Consultancy, InHouse, PublicSector, Agency, Startup, Recruitment, Broker }

// Bands instead of a headcount, which goes stale: 1–10, 11–50, 51–200, 201–1000, 1000+.
public enum CompanySize { Micro, Small, Medium, Large, Enterprise }

public class CompanySkill
{
    public int CompanyId { get; set; }
    public Company Company { get; set; } = null!;
    public int SkillId { get; set; }
    public Skill Skill { get; set; } = null!;
}

public class CompanyLocation
{
    public int CompanyId { get; set; }
    public Company Company { get; set; } = null!;
    public int LocationId { get; set; }
    public Location Location { get; set; } = null!;
}

public class CompanyConfiguration : IEntityTypeConfiguration<Company>
{
    public void Configure(EntityTypeBuilder<Company> builder)
    {
        builder.HasOne<AppUser>().WithMany().HasForeignKey(c => c.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(c => new { c.UserId, c.NormalizedName }).IsUnique();
        builder.HasOne(c => c.Industry).WithMany().HasForeignKey(c => c.IndustryId).OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(c => new { c.UserId, c.OrgNumber }).IsUnique().HasFilter("\"OrgNumber\" IS NOT NULL");
        builder.Property(c => c.OrgNumber).HasMaxLength(9);
    }
}

public class CompanySkillConfiguration : IEntityTypeConfiguration<CompanySkill>
{
    public void Configure(EntityTypeBuilder<CompanySkill> builder)
    {
        builder.HasKey(s => new { s.CompanyId, s.SkillId });
        builder.HasOne(s => s.Company).WithMany(c => c.Skills).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(s => s.Skill).WithMany().OnDelete(DeleteBehavior.Cascade);
    }
}

public class CompanyLocationConfiguration : IEntityTypeConfiguration<CompanyLocation>
{
    public void Configure(EntityTypeBuilder<CompanyLocation> builder)
    {
        builder.HasKey(l => new { l.CompanyId, l.LocationId });
        builder.HasOne(l => l.Company).WithMany(c => c.Locations).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(l => l.Location).WithMany().OnDelete(DeleteBehavior.Cascade);
    }
}
