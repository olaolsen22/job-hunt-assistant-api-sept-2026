using JobHunt.Api.Features.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobHunt.Api.Features.ListingImports;

// One generated JSON file from the import folder. Listings are only created when it runs through quick add.
public class ListingImport
{
    public int Id { get; set; }
    public string UserId { get; set; } = null!;
    public string FileName { get; set; } = null!;
    // The file name's suffix (it, generic, remote-it, agencies); free text so a new search needs no code change.
    public string Kind { get; set; } = null!;
    public DateTime GeneratedAt { get; set; }
    public DateTime ImportedAt { get; set; }
    public int ItemCount { get; set; }
    // The file as written, ready for quick add's JSON input. Null when the file is invalid.
    public string? Content { get; set; }
    public string? Error { get; set; }
    public DateTime? ReviewedAt { get; set; }
}

public class ListingImportConfiguration : IEntityTypeConfiguration<ListingImport>
{
    public void Configure(EntityTypeBuilder<ListingImport> builder)
    {
        builder.HasOne<AppUser>().WithMany().HasForeignKey(i => i.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.Property(i => i.FileName).HasMaxLength(200);
        builder.Property(i => i.Kind).HasMaxLength(50);
        // Unique: the scanner's "already imported" check, and a guard if two scans overlap.
        builder.HasIndex(i => new { i.UserId, i.FileName }).IsUnique();
        builder.HasIndex(i => new { i.UserId, i.ReviewedAt, i.GeneratedAt });
    }
}
