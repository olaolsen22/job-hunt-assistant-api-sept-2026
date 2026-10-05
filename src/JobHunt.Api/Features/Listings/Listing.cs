using JobHunt.Api.Features.Applications;
using JobHunt.Api.Features.Auth;
using JobHunt.Api.Features.Companies;
using JobHunt.Api.Features.Documents;
using JobHunt.Api.Features.JobRoles;
// Hot Chocolate also has a global Location type (a position in a query document).
using Location = JobHunt.Api.Features.Locations.Location;
using JobHunt.Api.Features.Skills;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobHunt.Api.Features.Listings;

public class Listing
{
    public int Id { get; set; }
    public string UserId { get; set; } = null!;
    public string Title { get; set; } = null!;
    public int CompanyId { get; set; }
    public Company Company { get; set; } = null!;
    public int JobRoleId { get; set; }
    public JobRole JobRole { get; set; } = null!;
    public Seniority? Seniority { get; set; }
    public EmploymentType? EmploymentType { get; set; }
    public Schedule? Schedule { get; set; }
    public WorkMode? WorkMode { get; set; }
    public string? Description { get; set; }
    public string Url { get; set; } = null!;
    public DateOnly PostedOn { get; set; }
    public DeadlineType DeadlineType { get; set; }
    public DateOnly? DeadlineOn { get; set; }
    public Language? PostingLanguage { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    // Null = not favorited. A time rather than a bool so the shortlist can sort by when you favorited.
    public DateTime? FavoritedAt { get; set; }
    // Null = active. Archived listings drop out of the default list but keep their history and documents.
    public DateTime? ArchivedAt { get; set; }
    // Null = details still to fill in. Set by the user (or ticked by the parse flow), not derived from which
    // fields happen to be set, so a hand-filled listing counts too. A time so the to-do list can sort by it.
    public DateTime? AnalyzedAt { get; set; }
    // Current status, kept next to its history so the list can filter and sort on it with plain SQL.
    // Only the application mutations change it, always together with a StatusChange row.
    public ApplicationStatus Status { get; set; }
    // OccurredAt of the latest status change; null while Saved.
    public DateTime? StatusChangedAt { get; set; }
    public List<ListingSkill> Skills { get; set; } = [];
    public List<ListingLanguage> Languages { get; set; } = [];
    // Several, because ads often list more than one office. Remote-anywhere listings have none.
    public List<ListingLocation> Locations { get; set; } = [];
    public List<Document> Documents { get; set; } = [];
    public List<StatusChange> StatusHistory { get; set; } = [];
    public List<Interview> Interviews { get; set; } = [];
}

public class ListingSkill
{
    public int ListingId { get; set; }
    public Listing Listing { get; set; } = null!;
    public int SkillId { get; set; }
    public Skill Skill { get; set; } = null!;
    // Required level, 1–5. There is no 0: a CV without the skill simply has no row, and matching counts that as 0.
    public int Level { get; set; }
    // Only when the ad states years ("5+ years of C#"). Kept apart from Level, which is our estimate,
    // so matching can compare against CV experience years directly.
    public int? MinYears { get; set; }
    public bool IsRequired { get; set; }
}

public class ListingLanguage
{
    public int ListingId { get; set; }
    public Listing Listing { get; set; } = null!;
    public Language Language { get; set; }
    public CefrLevel? Level { get; set; }
    public bool IsRequired { get; set; }
}

public class ListingLocation
{
    public int ListingId { get; set; }
    public Listing Listing { get; set; } = null!;
    public int LocationId { get; set; }
    public Location Location { get; set; } = null!;
}

public enum Seniority { Intern, Junior, Mid, Senior, Lead }

// Norwegian ads: Fast, Vikariat, Engasjement, Konsulent/Oppdrag/Frilans, Trainee/Lærling, Sesong.
public enum EmploymentType { Permanent, Temporary, FixedTerm, Contract, Internship, Seasonal }

// Separate from EmploymentType because Norwegian ads combine them ("Fast, deltid").
public enum Schedule { FullTime, PartTime }

public enum WorkMode { Onsite, Hybrid, Remote }

// Many ads have no date and say "Snarest"/"ASAP" or "Løpende" (rolling) instead.
public enum DeadlineType { Date, Asap, Rolling, Unspecified }

public enum Language { English, Norwegian }

public enum CefrLevel { A1, A2, B1, B2, C1, C2 }

public class ListingConfiguration : IEntityTypeConfiguration<Listing>
{
    public void Configure(EntityTypeBuilder<Listing> builder)
    {
        builder.HasOne<AppUser>().WithMany().HasForeignKey(l => l.UserId).OnDelete(DeleteBehavior.Cascade);
        // NoAction, not Restrict: SQLite checks NoAction at the end of the statement, so deleting a user
        // can cascade to its listings and its companies/roles in one go. Restrict can fail mid-cascade,
        // depending on which table SQLite happens to clear first.
        builder.HasOne(l => l.Company).WithMany().OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(l => l.JobRole).WithMany().OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(l => new { l.UserId, l.CreatedAt });
        builder.HasIndex(l => new { l.UserId, l.Status });
        // Unique among open listings only, so a reposted ad can be logged again once the earlier attempt is over.
        // Must match ApplicationStatuses.Final.
        builder.HasIndex(l => new { l.UserId, l.Url }).IsUnique()
            .HasFilter("\"Status\" NOT IN ('Rejected', 'Withdrawn', 'Accepted')");
    }
}

public class ListingSkillConfiguration : IEntityTypeConfiguration<ListingSkill>
{
    public void Configure(EntityTypeBuilder<ListingSkill> builder)
    {
        builder.HasKey(s => new { s.ListingId, s.SkillId });
        builder.HasOne(s => s.Listing).WithMany(l => l.Skills).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(s => s.Skill).WithMany().OnDelete(DeleteBehavior.Cascade);
    }
}

public class ListingLocationConfiguration : IEntityTypeConfiguration<ListingLocation>
{
    public void Configure(EntityTypeBuilder<ListingLocation> builder)
    {
        builder.HasKey(l => new { l.ListingId, l.LocationId });
        builder.HasOne(l => l.Listing).WithMany(l => l.Locations).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(l => l.Location).WithMany().OnDelete(DeleteBehavior.Cascade);
    }
}

public class ListingLanguageConfiguration : IEntityTypeConfiguration<ListingLanguage>
{
    public void Configure(EntityTypeBuilder<ListingLanguage> builder)
    {
        builder.HasKey(l => new { l.ListingId, l.Language });
        builder.HasOne(l => l.Listing).WithMany(l => l.Languages).OnDelete(DeleteBehavior.Cascade);
    }
}
