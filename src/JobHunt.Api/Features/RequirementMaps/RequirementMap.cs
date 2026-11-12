using System.Text.Json;
using JobHunt.Api.Features.Auth;
using JobHunt.Api.Features.Listings;
using JobHunt.Api.Features.TailoredCvDrafts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobHunt.Api.Features.RequirementMaps;

// What one listing needs and what the profile shows for it. One per listing, saved only after the user reviewed it.
// Named "requirement map" because a listing's AnalyzedAt already means parsed.
public class RequirementMap
{
    public int Id { get; set; }
    public string UserId { get; set; } = null!;
    public int ListingId { get; set; }
    public Listing Listing { get; set; } = null!;
    // Leaf data: the UI edits the map as a whole, and no query looks inside it.
    public string ContentJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public RequirementMapContent Content
    {
        get => JsonSerializer.Deserialize<RequirementMapContent>(ContentJson, TailoredCvJson.Options) ?? new();
        set => ContentJson = JsonSerializer.Serialize(value, TailoredCvJson.Options);
    }
}

public enum RequirementKind { Skill, Duty, Qualification, Language, Practical }

public enum RequirementImportance { Required, Preferred }

public enum RequirementMatch { Evidenced, Partial, Gap }

public enum RequirementEntryKind { Experience, Education, Certification, Project }

// An entry as evidence when no single bullet states the need, such as a degree or a skill tagged on a job.
public record RequirementEntryRef(RequirementEntryKind Kind, int Id);

// Evidence ids are a snapshot: checked against the profile when saved, never updated when the profile changes.
// No profile skill ids, so skill cleanup has nothing to rewrite here.
public record RequirementNeed(string Text, RequirementKind Kind, RequirementImportance Importance,
    RequirementMatch Match, List<int>? BulletIds = null, List<RequirementEntryRef>? Entries = null, string? Note = null)
{
    public List<int> BulletIds { get; init; } = BulletIds ?? [];
    public List<RequirementEntryRef> Entries { get; init; } = Entries ?? [];
}

public record RequirementMapContent(List<RequirementNeed>? Needs = null)
{
    // Never null, so empty JSON still reads as a map without needs.
    public List<RequirementNeed> Needs { get; init; } = Needs ?? [];
}

public class RequirementMapConfiguration : IEntityTypeConfiguration<RequirementMap>
{
    public void Configure(EntityTypeBuilder<RequirementMap> builder)
    {
        builder.Ignore(m => m.Content);
        builder.Property(m => m.ContentJson).HasColumnType("TEXT");
        builder.HasOne<AppUser>().WithMany().HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(m => m.Listing).WithMany().HasForeignKey(m => m.ListingId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(m => m.UserId);
        builder.HasIndex(m => m.ListingId).IsUnique();
    }
}

[ObjectType<RequirementMap>]
public static partial class RequirementMapNode
{
    static partial void Configure(IObjectTypeDescriptor<RequirementMap> descriptor)
    {
        descriptor.Ignore(m => m.UserId);
        descriptor.Ignore(m => m.ListingId);
        descriptor.Ignore(m => m.Listing);
        descriptor.Ignore(m => m.ContentJson);
    }
}
