using JobHunt.Api.Features.Auth;
using JobHunt.Api.Features.Listings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobHunt.Api.Features.Documents;

// Immutable once uploaded: a new version is a new upload, so a listing keeps the exact file that was sent.
public class Document
{
    public int Id { get; set; }
    public string UserId { get; set; } = null!;
    public int ListingId { get; set; }
    public Listing Listing { get; set; } = null!;
    public DocumentKind Kind { get; set; }
    // Generated from company, role, kind and date, so downloaded files are easy to find later.
    public string FileName { get; set; } = null!;
    public string OriginalFileName { get; set; } = null!;
    public long SizeBytes { get; set; }
    // SHA-256 of the bytes: the same hash means the exact same file, so CV versions can be compared across applications.
    public string ContentHash { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    // Null = not sent. Set by applyToListing; a sent document is the record of an application, so it can't be deleted.
    public DateTime? SentAt { get; set; }
}

// Its own table so loading documents (or listings with their documents) never pulls the file bytes.
public class DocumentContent
{
    public int DocumentId { get; set; }
    public Document Document { get; set; } = null!;
    public byte[] Bytes { get; set; } = null!;
}

public enum DocumentKind { Cv, CoverLetter }

public class DocumentConfiguration : IEntityTypeConfiguration<Document>
{
    public void Configure(EntityTypeBuilder<Document> builder)
    {
        builder.HasOne<AppUser>().WithMany().HasForeignKey(d => d.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(d => d.Listing).WithMany(l => l.Documents).OnDelete(DeleteBehavior.Cascade);
        builder.Property(d => d.ContentHash).HasMaxLength(64);
        builder.HasIndex(d => new { d.UserId, d.ContentHash });
    }
}

public class DocumentContentConfiguration : IEntityTypeConfiguration<DocumentContent>
{
    public void Configure(EntityTypeBuilder<DocumentContent> builder)
    {
        builder.HasKey(c => c.DocumentId);
        builder.HasOne(c => c.Document).WithOne().HasForeignKey<DocumentContent>(c => c.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
